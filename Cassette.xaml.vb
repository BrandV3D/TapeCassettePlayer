Imports System.IO
Imports System.Linq
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports NAudio.Wave

Class Cassette

    ' Playback engine (NAudio) - swapped in for MediaElement so the equalizer can actually filter
    ' the audio in real time; MediaElement has no way to expose samples for that.
    Private ReadOnly engine As New AudioEngine()

    ' Path of the audio file currently loaded/playing (mp3, flac, or wav; ~1 to 9 minutes).
    Private songPlaying As String = String.Empty

    ' Actual duration of songPlaying, read from the file once it opens.
    Private songLength As TimeSpan = TimeSpan.Zero

    Private ReadOnly LeftWheelRevolution As TimeSpan = TimeSpan.FromSeconds(3)
    Private ReadOnly RightWheelRevolution As TimeSpan = TimeSpan.FromSeconds(1.5)

    ' 0.001% of the 800px design-canvas width that CenterWindow/CenterWindow_Copy drift over a song.
    Private ReadOnly CenterWindowShift As Double = 800 * (0.001 / 100)

    ' Rewind / fast-forward: held down to seek continuously.
    Private ReadOnly seekTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(100)}
    Private seekDirection As Integer

    ' Refreshes the time-remaining / song-length display while a song is loaded.
    Private ReadOnly positionTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(250)}

    ' Recording (mic -> wav) via NAudio.
    Private isRecording As Boolean
    Private loopbackCapture As WasapiLoopbackCapture
    Private waveWriter As WaveFileWriter
    Private recordingPath As String

    ' Switch Tape: every design in the Cassetttes folder, wrapping back to the first after the last one.
    Private ReadOnly cassetteImagePaths As New List(Of String)
    Private cassetteImageIndex As Integer

    ' Film-tape texture: randomly tan or brown, re-rolled each time a tape is loaded/switched.
    Private ReadOnly filmTapeTextures As String() = {"NewFilmTapeBail.png", "NewFilmTapeBailBrown.png"}
    Private ReadOnly rng As New Random()

    ' Extra folders/files added via Open File / Open Folder, outside the app's own directory.
    ' Kept across LoadPlaylist() refreshes (e.g. after a recording finishes) so they aren't lost.
    Private ReadOnly extraFolders As New List(Of String)
    Private ReadOnly extraFiles As New List(Of String)
    Private ReadOnly AudioExtensions As String() = {"*.mp3", "*.wav", "*.flac"}

    ' Flip Tape: Side A is whatever's normally playing; Side B is a second track, flipped to the
    ' other side visually (StretchSongItem_Copy instead of StretchSongItem). sideATrackPath
    ' remembers Side A's track so flipping back resumes it instead of starting over from scratch.
    Private isSideB As Boolean
    Private sideATrackPath As String

    ' Points at whichever of StretchScaleTransform/StretchScaleTransform1 is currently active, so
    ' the stretch-animation code below doesn't need to know which side is showing. Side A's scale
    ' runs +1 to +10; Side B's element starts at ScaleX=-1 in XAML and mirrors as it grows, so it
    ' needs the same animation driven -1 to -10 instead - currentStretchSign (+1/-1) is multiplied
    ' into every value the animation code computes so one set of methods covers both.
    Private currentStretchTransform As ScaleTransform
    Private currentStretchSign As Double = 1

    ' Mechanical deck sound effects (button clunks, eject/insert, rewind/FF motor whir).
    Private ReadOnly sfx As New SfxPlayer()

    ' Volume / Mute: volumeBeforeMute remembers the slider's level so unmuting restores it exactly,
    ' rather than just leaving the muted 0 in place.
    Private isMuted As Boolean
    Private volumeBeforeMute As Double = 0.5

    ' True only while paused (via the Pause button) - distinct from stopped: the engine, wheels,
    ' and stretch/lines/center-window animations are all frozen exactly where they were rather than
    ' reset, so the same button can resume from here.
    Private isPaused As Boolean

    ' The tape visual (this window), the transport controls, and the playlist are three
    ' independent, freely movable/resizable windows; Cassette owns the other two and holds
    ' all the playback/recording/animation logic for all three.
    Private ReadOnly controlsWindow As New ControlsWindow()
    Private ReadOnly playlistWindow As New PlaylistWindow()
    Private ReadOnly albumArtWindow As New AlbumArtWindow()
    Private ReadOnly equalizerWindow As New EqualizerWindow()
    Private ReadOnly visualizerWindow As New VisualizerWindow()
    Private ReadOnly equalizerControlsWindow As New EqualizerControlsWindow(engine)
    Private ReadOnly themeWindow As New ThemeWindow()

    ' Purely cosmetic panels - no playback logic, just decoration the user can freely dock, float,
    ' or leave out of their own layout (see MainWindow.xaml's LayoutAnchorable CanClose="True" for
    ' these, unlike the seven functional panels above).
    Private ReadOnly linerNotesWindow As New LinerNotesWindow()
    Private ReadOnly vuMetersWindow As New VuMetersWindow()
    Private ReadOnly stickersWindow As New StickersWindow()
    Private ReadOnly staticNoiseWindow As New StaticNoiseWindow()
    Private ReadOnly tapeCounterWindow As New TapeCounterWindow()
    Private ReadOnly concertPosterWindow As New ConcertPosterWindow()
    Private ReadOnly retroAdWindow As New RetroAdWindow()
    Private ReadOnly creditsWindow As New CreditsWindow()

    ''' <summary>Exposes playlistWindow's ListBox under the name every existing playlist call site
    ''' already uses, so PlaySong/LoadPlaylist/etc. didn't need to change when the ListBox moved
    ''' out of this window's own XAML.</summary>
    Private ReadOnly Property PlaylistBox As ListBox
        Get
            Return playlistWindow.PlaylistBox
        End Get
    End Property

    ''' <summary>The three properties below expose controlsWindow's elements under the names every
    ''' existing control-handling call site already uses, so that code didn't need to change when
    ''' the transport controls moved out of this window's own XAML.</summary>
    Private ReadOnly Property StatusText As TextBlock
        Get
            Return controlsWindow.StatusText
        End Get
    End Property

    Private ReadOnly Property TimeText As TextBlock
        Get
            Return controlsWindow.TimeText
        End Get
    End Property

    Private ReadOnly Property RecordButton As Button
        Get
            Return controlsWindow.RecordButton
        End Get
    End Property

    Public Sub New()
        InitializeComponent()
        currentStretchTransform = StretchScaleTransform
        AddHandler seekTimer.Tick, AddressOf SeekTimer_Tick
        AddHandler positionTimer.Tick, AddressOf PositionTimer_Tick
        AddHandler engine.PlaybackEnded, AddressOf Engine_PlaybackEnded
        WireControlsWindow()
        AddHandler playlistWindow.RefreshRequested, AddressOf LoadPlaylist
        LoadPlaylist()
        LoadCassetteImages()
    End Sub

    ''' <summary>The transport buttons live in ControlsWindow's own XAML/class, but every handler
    ''' for them stays here alongside the rest of the playback logic, so they're wired up by code
    ''' instead of Click="..." in ControlsWindow's XAML.</summary>
    Private Sub WireControlsWindow()
        AddHandler controlsWindow.RewindButton.PreviewMouseLeftButtonDown, AddressOf RewindButton_MouseDown
        AddHandler controlsWindow.RewindButton.PreviewMouseLeftButtonUp, AddressOf SeekButton_MouseUp
        AddHandler controlsWindow.RewindButton.LostMouseCapture, AddressOf SeekButton_LostMouseCapture
        AddHandler controlsWindow.PreviousButton.Click, AddressOf PreviousButton_Click
        AddHandler controlsWindow.PlayButton.Click, AddressOf PlayButton_Click
        AddHandler controlsWindow.PauseButton.Click, AddressOf PauseButton_Click
        AddHandler controlsWindow.StopButton.Click, AddressOf StopButton_Click
        AddHandler controlsWindow.NextButton.Click, AddressOf NextButton_Click
        AddHandler controlsWindow.FastForwardButton.PreviewMouseLeftButtonDown, AddressOf FastForwardButton_MouseDown
        AddHandler controlsWindow.FastForwardButton.PreviewMouseLeftButtonUp, AddressOf SeekButton_MouseUp
        AddHandler controlsWindow.FastForwardButton.LostMouseCapture, AddressOf SeekButton_LostMouseCapture
        AddHandler controlsWindow.RecordButton.Click, AddressOf RecordButton_Click
        AddHandler controlsWindow.SwitchTapeButton.Click, AddressOf SwitchTapeButton_Click
        AddHandler controlsWindow.FlipTapeButton.Click, AddressOf FlipTapeButton_Click
        AddHandler controlsWindow.OpenFileButton.Click, AddressOf OpenFileButton_Click
        AddHandler controlsWindow.OpenFolderButton.Click, AddressOf OpenFolderButton_Click
        AddHandler controlsWindow.MixtapeButton.Click, AddressOf MixtapeButton_Click
        AddHandler controlsWindow.LoadMixtapeButton.Click, AddressOf LoadMixtapeButton_Click
        AddHandler controlsWindow.MuteButton.Click, AddressOf MuteButton_Click
        AddHandler controlsWindow.VolumeSlider.ValueChanged, AddressOf VolumeSlider_ValueChanged
        engine.Volume = CSng(controlsWindow.VolumeSlider.Value)
    End Sub

    ''' <summary>The other six panels are exposed here so MainWindow can slot each one into its own
    ''' AvalonDock LayoutAnchorable - Cassette still owns them (creates them, wires their buttons,
    ''' holds all the playback/recording/animation logic) exactly as when they were separate
    ''' top-level windows; only how they get displayed changed.</summary>
    Public ReadOnly Property ControlsView As ControlsWindow
        Get
            Return controlsWindow
        End Get
    End Property

    Public ReadOnly Property PlaylistView As PlaylistWindow
        Get
            Return playlistWindow
        End Get
    End Property

    Public ReadOnly Property AlbumArtView As AlbumArtWindow
        Get
            Return albumArtWindow
        End Get
    End Property

    Public ReadOnly Property EqualizerView As EqualizerWindow
        Get
            Return equalizerWindow
        End Get
    End Property

    Public ReadOnly Property VisualizerView As VisualizerWindow
        Get
            Return visualizerWindow
        End Get
    End Property

    Public ReadOnly Property EqualizerControlsView As EqualizerControlsWindow
        Get
            Return equalizerControlsWindow
        End Get
    End Property

    Public ReadOnly Property ThemeView As ThemeWindow
        Get
            Return themeWindow
        End Get
    End Property

    Public ReadOnly Property LinerNotesView As LinerNotesWindow
        Get
            Return linerNotesWindow
        End Get
    End Property

    Public ReadOnly Property VuMetersView As VuMetersWindow
        Get
            Return vuMetersWindow
        End Get
    End Property

    Public ReadOnly Property StickersView As StickersWindow
        Get
            Return stickersWindow
        End Get
    End Property

    Public ReadOnly Property StaticNoiseView As StaticNoiseWindow
        Get
            Return staticNoiseWindow
        End Get
    End Property

    Public ReadOnly Property TapeCounterView As TapeCounterWindow
        Get
            Return tapeCounterWindow
        End Get
    End Property

    Public ReadOnly Property ConcertPosterView As ConcertPosterWindow
        Get
            Return concertPosterWindow
        End Get
    End Property

    Public ReadOnly Property RetroAdView As RetroAdWindow
        Get
            Return retroAdWindow
        End Get
    End Property

    Public ReadOnly Property CreditsView As CreditsWindow
        Get
            Return creditsWindow
        End Get
    End Property

    ''' <summary>Called by MainWindow's own Closing handler - Cassette is a UserControl now (hosted
    ''' in a dock pane) and has no Closing event of its own.</summary>
    Public Sub Cleanup()
        If isRecording Then StopRecording()
        sfx.StopLoop()
        engine.Dispose()
    End Sub

    ' ---- Playlist ----------------------------------------------------

    Private Sub LoadPlaylist()
        Dim previousTag As String = TryCast(TryCast(PlaylistBox.SelectedItem, ListBoxItem)?.Tag, String)

        PlaylistBox.Items.Clear()

        Dim baseDir = AppDomain.CurrentDomain.BaseDirectory
        Dim searchDirs = {baseDir}.Concat(extraFolders).Distinct(StringComparer.OrdinalIgnoreCase)

        Dim files = searchDirs.
            Where(Function(dir) Directory.Exists(dir)).
            SelectMany(Function(dir) AudioExtensions.SelectMany(Function(ext) Directory.GetFiles(dir, ext))).
            Concat(extraFiles.Where(Function(f) File.Exists(f))).
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(f) Path.GetFileName(f)).
            ToList()

        For Each f In files
            PlaylistBox.Items.Add(New ListBoxItem With {.Content = Path.GetFileName(f), .Tag = f})
        Next

        Dim itemToSelect = PlaylistBox.Items.OfType(Of ListBoxItem)().
            FirstOrDefault(Function(i) CStr(i.Tag) = previousTag)

        If itemToSelect Is Nothing Then
            itemToSelect = PlaylistBox.Items.OfType(Of ListBoxItem)().
                FirstOrDefault(Function(i) Path.GetFileName(CStr(i.Tag)).Equals("song.mp3", StringComparison.OrdinalIgnoreCase))
        End If

        If itemToSelect IsNot Nothing Then
            PlaylistBox.SelectedItem = itemToSelect
        ElseIf PlaylistBox.Items.Count > 0 Then
            PlaylistBox.SelectedIndex = 0
        End If
    End Sub

    ' ---- Switch Tape (cycle the cassette face art) ----------------------

    ''' <summary>Builds the cassette face rotation from every design under the Cassetttes folder,
    ''' in name order, and shows the first one.</summary>
    Private Sub LoadCassetteImages()
        Dim baseDir = AppDomain.CurrentDomain.BaseDirectory

        Dim cassettesDir = Path.Combine(baseDir, "Cassetttes")

        cassetteImagePaths.Clear()
        If Directory.Exists(cassettesDir) Then
            cassetteImagePaths.AddRange(
                Directory.GetFiles(cassettesDir, "*.png").
                OrderBy(Function(f) Path.GetFileName(f)))
        End If

        cassetteImageIndex = 0
        ApplyCassetteImage()
    End Sub

    Private Sub ApplyCassetteImage()
        If cassetteImagePaths.Count = 0 Then Return
        Dim path = cassetteImagePaths(cassetteImageIndex)
        If Not File.Exists(path) Then Return
        background.Source = New BitmapImage(New Uri(path))
        ApplyRandomFilmTexture()
    End Sub

    ''' <summary>Randomly picks the tan or brown film-tape texture for this tape, so different
    ''' tapes in the rotation don't all look identical.</summary>
    Private Sub ApplyRandomFilmTexture()
        Dim texture = filmTapeTextures(rng.Next(filmTapeTextures.Length))
        StretchSongItem.Source = New BitmapImage(New Uri($"pack://application:,,,/{texture}"))
    End Sub

    Private Sub SwitchTapeButton_Click(sender As Object, e As RoutedEventArgs)
        If cassetteImagePaths.Count = 0 Then Return
        sfx.Play("eject.wav")
        cassetteImageIndex = (cassetteImageIndex + 1) Mod cassetteImagePaths.Count
        ApplyCassetteImage()
    End Sub

    ' ---- Flip Tape (Side A / Side B) -------------------------------------

    ''' <summary>Flips the tape over: Side A is whatever was already playing (remembered so
    ''' flipping back resumes it); Side B is the next track in the playlist. Either way, swaps
    ''' which of StretchSongItem/StretchSongItem_Copy is showing to match.</summary>
    Private Sub FlipTapeButton_Click(sender As Object, e As RoutedEventArgs)
        If isSideB Then
            sfx.Play("insert.wav")
            SetTapeSide(sideB:=False)
            If Not String.IsNullOrEmpty(sideATrackPath) Then PlaySong(sideATrackPath)
        Else
            If PlaylistBox.Items.Count < 2 Then
                StatusText.Text = "Add another track to the playlist to use Side B"
                Return
            End If

            sfx.Play("insert.wav")
            sideATrackPath = songPlaying
            SetTapeSide(sideB:=True)
            AdvancePlaylist(1)
        End If
    End Sub

    ''' <summary>Switches which side's tape-stretch visual is showing and which transform the
    ''' stretch-animation methods below drive, then resets it to its at-rest state.</summary>
    Private Sub SetTapeSide(sideB As Boolean)
        isSideB = sideB
        StretchSongItem.Visibility = If(sideB, Visibility.Collapsed, Visibility.Visible)
        StretchSongItem_Copy.Visibility = If(sideB, Visibility.Visible, Visibility.Collapsed)
        currentStretchTransform = If(sideB, StretchScaleTransform1, StretchScaleTransform)
        currentStretchSign = If(sideB, -1, 1)
        controlsWindow.FlipTapeButton.ToolTip = If(sideB, "Flip to Side A", "Flip to Side B")
        ResetStretch()
    End Sub

    ' ---- Open File / Open Folder ----------------------------------------

    Private Sub OpenFileButton_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New Microsoft.Win32.OpenFileDialog With {
            .Title = "Open Audio File",
            .Filter = "Audio Files (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac|All Files (*.*)|*.*",
            .CheckFileExists = True
        }
        If dialog.ShowDialog(Window.GetWindow(Me)) = True Then
            OpenAudioFile(dialog.FileName)
        End If
    End Sub

    Private Sub OpenFolderButton_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New Microsoft.Win32.OpenFolderDialog With {
            .Title = "Open Folder"
        }
        If dialog.ShowDialog(Window.GetWindow(Me)) = True Then
            OpenAudioFolder(dialog.FolderName)
        End If
    End Sub

    ''' <summary>Adds an audio file from anywhere on disk to the playlist and starts playing it.</summary>
    Public Sub OpenAudioFile(filePath As String)
        If Not File.Exists(filePath) Then
            StatusText.Text = "File not found: " & filePath
            Return
        End If

        If Not extraFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase) Then
            extraFiles.Add(filePath)
        End If

        LoadPlaylist()
        SelectPlaylistItem(filePath)
        PlaySong(filePath)
    End Sub

    ''' <summary>Adds a folder's audio files (mp3/wav/flac) to the playlist and starts playing the first one found.</summary>
    Public Sub OpenAudioFolder(folderPath As String)
        If Not Directory.Exists(folderPath) Then
            StatusText.Text = "Folder not found: " & folderPath
            Return
        End If

        If Not extraFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase) Then
            extraFolders.Add(folderPath)
        End If

        LoadPlaylist()

        Dim firstInFolder = PlaylistBox.Items.OfType(Of ListBoxItem)().
            FirstOrDefault(Function(i) Path.GetDirectoryName(CStr(i.Tag)).Equals(folderPath, StringComparison.OrdinalIgnoreCase))

        If firstInFolder Is Nothing Then
            StatusText.Text = "No audio files found in: " & folderPath
            Return
        End If

        PlaylistBox.SelectedItem = firstInFolder
        PlaySong(CStr(firstInFolder.Tag))
    End Sub

    Private Sub SelectPlaylistItem(filePath As String)
        Dim item = PlaylistBox.Items.OfType(Of ListBoxItem)().
            FirstOrDefault(Function(i) CStr(i.Tag).Equals(filePath, StringComparison.OrdinalIgnoreCase))
        If item IsNot Nothing Then PlaylistBox.SelectedItem = item
    End Sub

    ' ---- Mixtape (pick songs, bake them into 60minTape.mp3/90minTape.mp3) -----

    ''' <summary>Opens the mixtape builder as a fresh window each time, since a closed WPF Window
    ''' can't be shown again; refreshes the playlist once it reports a tape was written so the
    ''' new 60minTape.mp3/90minTape.mp3 shows up immediately.</summary>
    Private Sub MixtapeButton_Click(sender As Object, e As RoutedEventArgs)
        Dim mixtapeWindow As New MixtapeWindow With {.Owner = Window.GetWindow(Me)}
        AddHandler mixtapeWindow.MixtapeBuilt, AddressOf MixtapeWindow_MixtapeBuilt
        mixtapeWindow.ShowDialog()
    End Sub

    Private Sub MixtapeWindow_MixtapeBuilt(outputPath As String)
        LoadPlaylist()
    End Sub

    ''' <summary>Opens the mixtape picker as a fresh window each time; loading a mixtape adds it
    ''' to the playlist (like Open File) and starts it playing right away.</summary>
    Private Sub LoadMixtapeButton_Click(sender As Object, e As RoutedEventArgs)
        Dim loadWindow As New LoadMixtapeWindow With {.Owner = Window.GetWindow(Me)}
        AddHandler loadWindow.MixtapeSelected, AddressOf LoadMixtapeWindow_MixtapeSelected
        loadWindow.ShowDialog()
    End Sub

    Private Sub LoadMixtapeWindow_MixtapeSelected(tapePath As String)
        OpenAudioFile(tapePath)
    End Sub

    ' ---- Play / Stop ---------------------------------------------------

    ''' <summary>Call this whenever a new song starts playing to sync the wheel/tape animation to it.</summary>
    Public Sub PlaySong(filePath As String)
        If Not File.Exists(filePath) Then
            StatusText.Text = "File not found: " & filePath
            Return
        End If

        Try
            engine.Load(filePath)
        Catch ex As Exception
            StatusText.Text = "Audio failed to load: " & ex.Message
            Return
        End Try

        ResetPauseButtonState()
        songPlaying = filePath
        ResetStretch()
        ResetLines()
        ResetCenterWindow()
        engine.Play()
        SpinWheels()
        positionTimer.Start()
        StatusText.Text = "Playing: " & Path.GetFileName(filePath)
        albumArtWindow.SetArt(AlbumArtReader.TryGetAlbumArt(filePath))
        UpdateMixtapeLabelText(filePath)
        equalizerWindow.SetPlaying(True)
        visualizerWindow.SetPlaying(True)

        ' NAudio's AudioFileReader parses the header synchronously, so - unlike MediaElement's
        ' MediaOpened - the duration is already known right here; no separate "opened" event needed.
        songLength = engine.Duration
        AnimateStretch(fromScale:=1, duration:=songLength)
        AnimateLines(fromScale:=1, duration:=songLength)
        AnimateCenterWindow(fromOffset:=0, duration:=songLength)
        UpdateTimeDisplay()
    End Sub

    ''' <summary>Shows the loaded mixtape's embedded label (MixtapeBuilder's TIT2 title) at the top
    ''' of the cassette window, rendered in whatever font it was labeled with (TXXX:LabelFont);
    ''' "Untitled" in the default font for a plain song or an unlabeled mixtape (ReadLabel already
    ''' returns Nothing/blank for either case).</summary>
    Private Sub UpdateMixtapeLabelText(filePath As String)
        Dim tags = MixtapeBuilder.ReadLabel(filePath)
        MixtapeLabelText.Text = If(String.IsNullOrWhiteSpace(tags.Label), "Untitled", tags.Label)
        MixtapeLabelText.FontFamily = New FontFamily(If(String.IsNullOrWhiteSpace(tags.FontFamily), "Segoe UI", tags.FontFamily))
    End Sub

    Private Sub PlayButton_Click(sender As Object, e As RoutedEventArgs)
        sfx.Play("clunk.wav")
        Dim selected = TryCast(PlaylistBox.SelectedItem, ListBoxItem)
        Dim songPath = If(selected IsNot Nothing,
                          CStr(selected.Tag),
                          Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "song.mp3"))
        PlaySong(songPath)
    End Sub

    Private Sub PauseButton_Click(sender As Object, e As RoutedEventArgs)
        If Not engine.IsLoaded Then Return
        If isPaused Then
            ResumeFromPause()
        Else
            PausePlayback()
        End If
    End Sub

    ''' <summary>Freezes audio, wheel spin, and the tape-stretch/Lines/CenterWindow_Copy animations
    ''' exactly where they are (reusing the same FreezeAnimation helper StopWheels already uses for
    ''' the wheels) - unlike Stop, nothing is reset, so ResumeFromPause can continue from here.</summary>
    Private Sub PausePlayback()
        isPaused = True
        sfx.Play("clunk.wav")
        positionTimer.Stop()
        engine.Pause()
        StopWheels()
        FreezeAnimation(currentStretchTransform, ScaleTransform.ScaleXProperty)
        FreezeAnimation(LinesScaleTransform, ScaleTransform.ScaleYProperty)
        FreezeAnimation(CenterWindowCopyTranslateTransform, TranslateTransform.XProperty)
        StatusText.Text = "Paused"
        controlsWindow.PauseButton.Content = "▶"
        controlsWindow.PauseButton.ToolTip = "Resume"
    End Sub

    ''' <summary>Resumes exactly where PausePlayback left off - ResumeWheels already knows how to
    ''' restart the wheel spin and the stretch/Lines/CenterWindow_Copy animations for whatever time
    ''' remains in the song, continuing from wherever they were frozen.</summary>
    Private Sub ResumeFromPause()
        isPaused = False
        sfx.Play("clunk.wav")
        engine.Play()
        ResumeWheels()
        positionTimer.Start()
        StatusText.Text = "Playing: " & Path.GetFileName(songPlaying)
        controlsWindow.PauseButton.Content = "⏸"
        controlsWindow.PauseButton.ToolTip = "Pause"
    End Sub

    ''' <summary>Puts the Pause button back to its default "not paused" glyph/tooltip - called
    ''' whenever playback starts fresh or stops, so it doesn't keep showing "Resume" from a pause
    ''' that Play/Stop/Next/Previous just overrode.</summary>
    Private Sub ResetPauseButtonState()
        isPaused = False
        controlsWindow.PauseButton.Content = "⏸"
        controlsWindow.PauseButton.ToolTip = "Pause"
    End Sub

    Private Sub StopButton_Click(sender As Object, e As RoutedEventArgs)
        sfx.Play("release.wav")
        StopPlayback()
    End Sub

    Private Sub PreviousButton_Click(sender As Object, e As RoutedEventArgs)
        sfx.Play("click.wav")
        AdvancePlaylist(-1)
    End Sub

    Private Sub NextButton_Click(sender As Object, e As RoutedEventArgs)
        sfx.Play("click.wav")
        AdvancePlaylist(1)
    End Sub

    ''' <summary>Moves the playlist selection by <paramref name="direction"/> (wrapping around) and plays it.</summary>
    Private Sub AdvancePlaylist(direction As Integer)
        If PlaylistBox.Items.Count = 0 Then Return

        Dim newIndex = PlaylistBox.SelectedIndex + direction
        If newIndex < 0 Then newIndex = PlaylistBox.Items.Count - 1
        If newIndex >= PlaylistBox.Items.Count Then newIndex = 0
        PlaylistBox.SelectedIndex = newIndex

        Dim selected = TryCast(PlaylistBox.SelectedItem, ListBoxItem)
        If selected IsNot Nothing Then
            PlaySong(CStr(selected.Tag))
        End If
    End Sub

    ''' <summary>Stops playback and freezes the wheels in place (no song loaded).</summary>
    Private Sub StopPlayback()
        ResetPauseButtonState()
        isSeeking = False
        seekTimer.Stop()
        sfx.StopLoop()
        positionTimer.Stop()
        engine.Stop()
        StatusText.Text = "Stopped"
        songLength = TimeSpan.Zero
        TimeText.Text = String.Empty
        ResetStretch()
        ResetLines()
        ResetCenterWindow()
        StopWheels()
        equalizerWindow.SetPlaying(False)
        visualizerWindow.SetPlaying(False)
    End Sub

    ' ---- Volume / Mute ---------------------------------------------------

    Private Sub VolumeSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        controlsWindow.VolumeText.Text = $"{CInt(Math.Round(e.NewValue * 100))}%"

        If isMuted Then
            ' Dragging the slider while muted unmutes automatically - matches how every other
            ' volume control (Windows, browsers, media players) behaves.
            isMuted = False
            controlsWindow.MuteButton.Content = "🔊"
        End If

        volumeBeforeMute = e.NewValue
        engine.Volume = CSng(e.NewValue)
    End Sub

    Private Sub MuteButton_Click(sender As Object, e As RoutedEventArgs)
        isMuted = Not isMuted
        If isMuted Then
            engine.Volume = 0
            controlsWindow.MuteButton.Content = "🔇"
        Else
            engine.Volume = CSng(volumeBeforeMute)
            controlsWindow.MuteButton.Content = "🔊"
        End If
    End Sub

    ''' <summary>Shows time remaining and total song length, e.g. "-2:15 / 3:45". Refreshed on a
    ''' timer during playback and seeking alike, since both move engine.Position.</summary>
    Private Sub PositionTimer_Tick(sender As Object, e As EventArgs)
        UpdateTimeDisplay()
    End Sub

    Private Sub UpdateTimeDisplay()
        If songLength <= TimeSpan.Zero Then
            TimeText.Text = String.Empty
            Return
        End If

        Dim remaining = songLength - engine.Position
        If remaining < TimeSpan.Zero Then remaining = TimeSpan.Zero
        TimeText.Text = $"-{remaining:mm\:ss} / {songLength:mm\:ss}"
    End Sub

    ''' <summary>engine.PlaybackEnded only fires when a track finishes on its own (not when Stop or
    ''' a new Load caused playback to stop) - mirrors what MediaElement's MediaEnded used to do.
    ''' Raised off NAudio's own callback thread's captured SynchronizationContext, which should
    ''' already be the UI thread's, but Dispatcher.Invoke keeps that guaranteed either way.</summary>
    Private Sub Engine_PlaybackEnded()
        Dispatcher.Invoke(AddressOf StopPlayback)
    End Sub

    ' ---- Wheel / tape-stretch animation ---------------------------------

    ''' <summary>Spins both wheels counter-clockwise forever, the left one slower than the right one.
    ''' Runs only while a song is actually playing or a recording is in progress — never on initial
    ''' window load, and it's frozen in place again as soon as Stop is pressed.</summary>
    Private Sub SpinWheels()
        AnimateWheel(LeftWheelRotateTransform, LeftWheelRevolution)
        AnimateWheel(RightWheelRotateTransform, RightWheelRevolution)
    End Sub

    ''' <summary>Freezes both wheels wherever they currently are (Stop pressed, recording finished).</summary>
    Private Sub StopWheels()
        FreezeAnimation(LeftWheelRotateTransform, RotateTransform.AngleProperty)
        FreezeAnimation(RightWheelRotateTransform, RotateTransform.AngleProperty)
    End Sub

    ''' <summary>Rotates <paramref name="transform"/> a full turn every <paramref name="revolutionLength"/>,
    ''' looping forever, continuing from its current angle. Counter-clockwise (normal play direction)
    ''' unless <paramref name="reverse"/> is set, which spins it clockwise instead.</summary>
    Private Sub AnimateWheel(transform As RotateTransform, revolutionLength As TimeSpan, Optional reverse As Boolean = False)
        Dim fromAngle = transform.Angle
        Dim anim As New DoubleAnimation With {
            .From = fromAngle,
            .To = fromAngle + If(reverse, 360, -360),
            .Duration = New Duration(revolutionLength),
            .RepeatBehavior = RepeatBehavior.Forever
        }
        transform.BeginAnimation(RotateTransform.AngleProperty, anim)
    End Sub

    ' How much faster each wheel spins during rewind/fast-forward than during normal playback.
    Private ReadOnly LeftWheelSeekMultiplier As Double = 4
    Private ReadOnly RightWheelSeekMultiplier As Double = 8

    ''' <summary>Spins both wheels rapidly to mimic tape winding while rewind/fast-forward is held:
    ''' backwards (clockwise) for rewind, forwards (counter-clockwise) for fast-forward. The left
    ''' wheel spins at 4x its normal speed, the right wheel at 8x.</summary>
    Private Sub SpinWheelsSeeking(direction As Integer)
        Dim reverse = direction < 0
        AnimateWheel(LeftWheelRotateTransform, TimeSpan.FromTicks(CLng(LeftWheelRevolution.Ticks / LeftWheelSeekMultiplier)), reverse)
        AnimateWheel(RightWheelRotateTransform, TimeSpan.FromTicks(CLng(RightWheelRevolution.Ticks / RightWheelSeekMultiplier)), reverse)
    End Sub

    ''' <summary>Resumes normal wheel spin and, if a song is loaded, the tape stretch, Lines scale, and
    ''' CenterWindow_Copy drift for their remaining duration — all continuing from wherever SpinWheelsSeeking left them.</summary>
    Private Sub ResumeWheels()
        SpinWheels()
        If songLength > TimeSpan.Zero Then
            Dim remaining = songLength - engine.Position
            ' currentStretchTransform.ScaleX is the signed value (negative on Side B); AnimateStretch
            ' wants the unsigned magnitude and applies currentStretchSign itself, so undo the sign here.
            AnimateStretch(currentStretchTransform.ScaleX * currentStretchSign, remaining)
            AnimateLines(LinesScaleTransform.ScaleY, remaining)
            AnimateCenterWindow(CenterWindowCopyTranslateTransform.X, remaining)
        End If
    End Sub

    Private Sub FreezeAnimation(target As Animatable, prop As DependencyProperty)
        Dim current = target.GetValue(prop)
        target.BeginAnimation(prop, Nothing)
        target.SetValue(prop, current)
    End Sub

    ''' <summary>Grows the active side's stretch item (StretchSongItem or StretchSongItem_Copy,
    ''' per <see cref="currentStretchTransform"/>) from <paramref name="fromScale"/> to 10x
    ''' magnitude, anchored at its outer edge so it stretches inward, over <paramref name="duration"/>.
    ''' <paramref name="fromScale"/> is always an unsigned magnitude (1 to 10); currentStretchSign
    ''' is applied here so Side B's element (which mirrors via a negative ScaleX) animates -1 to
    ''' -10 while Side A's animates +1 to +10, using the same call site.</summary>
    Private Sub AnimateStretch(fromScale As Double, duration As TimeSpan)
        If duration <= TimeSpan.Zero Then Return
        Dim anim As New DoubleAnimation With {
            .From = fromScale * currentStretchSign,
            .To = 10 * currentStretchSign,
            .Duration = New Duration(duration)
        }
        currentStretchTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim)
    End Sub

    Private Sub ResetStretch()
        currentStretchTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Nothing)
        currentStretchTransform.ScaleX = 1 * currentStretchSign
    End Sub

    ''' <summary>Sets the active side's stretch item scale directly from <paramref name="position"/>
    ''' within the song (bypassing the normal timeline animation), so while rewind/fast-forward is
    ''' held it tracks exactly where the seek currently is instead of drifting on its own independent clock.</summary>
    Private Sub UpdateStretchForPosition(position As TimeSpan)
        If songLength <= TimeSpan.Zero Then Return
        Dim progress = position.TotalSeconds / songLength.TotalSeconds
        progress = Math.Max(0, Math.Min(1, progress))
        currentStretchTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Nothing)
        currentStretchTransform.ScaleX = (1 + progress * 9) * currentStretchSign
    End Sub

    ''' <summary>Shrinks Lines from <paramref name="fromScale"/> down to half its height and back to full
    ''' height, once, spread evenly across <paramref name="duration"/> (the song's length).</summary>
    Private Sub AnimateLines(fromScale As Double, duration As TimeSpan)
        If duration <= TimeSpan.Zero Then Return
        Dim anim As New DoubleAnimation With {
            .From = fromScale,
            .To = 0.5,
            .Duration = New Duration(TimeSpan.FromTicks(Math.Max(duration.Ticks \ 2, 1))),
            .AutoReverse = True
        }
        LinesScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim)
    End Sub

    Private Sub ResetLines()
        LinesScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Nothing)
        LinesScaleTransform.ScaleY = 1
    End Sub

    ''' <summary>Drifts CenterWindow_Copy from <paramref name="fromOffset"/> to CenterWindowShift
    ''' (0.001% of the design-canvas width), once, over <paramref name="duration"/> (the song's length).</summary>
    Private Sub AnimateCenterWindow(fromOffset As Double, duration As TimeSpan)
        If duration <= TimeSpan.Zero Then Return
        CenterWindowCopyTranslateTransform.BeginAnimation(TranslateTransform.XProperty, New DoubleAnimation With {
            .From = fromOffset,
            .To = CenterWindowShift,
            .Duration = New Duration(duration)
        })
    End Sub

    Private Sub ResetCenterWindow()
        CenterWindowCopyTranslateTransform.BeginAnimation(TranslateTransform.XProperty, Nothing)
        CenterWindowCopyTranslateTransform.X = 0
    End Sub

    ' ---- Rewind / Fast-forward (hold to seek) --------------------------

    ' True only while a seek is actively in progress (mouse held on Rewind/FF).
    Private isSeeking As Boolean

    Private Sub RewindButton_MouseDown(sender As Object, e As MouseButtonEventArgs)
        BeginSeeking(DirectCast(sender, UIElement), -1)
    End Sub

    Private Sub FastForwardButton_MouseDown(sender As Object, e As MouseButtonEventArgs)
        BeginSeeking(DirectCast(sender, UIElement), 1)
    End Sub

    ''' <summary>Starts seeking: pauses real playback (so the only audio you hear while winding is
    ''' the seek_whir.wav sound effect, same as a real tape deck muting itself during fast-wind)
    ''' while the timer keeps yanking the position in the seek direction, captures the mouse so
    ''' releasing outside the button still ends the seek, and spins the wheels fast in that
    ''' direction. Takes one seek step immediately so a quick click seeks too, not just a held-down
    ''' press. Pausing (rather than the old speed-up-and-keep-playing approach) also means nothing
    ''' is reading from the engine while SeekTimer_Tick is jamming its position around.</summary>
    Private Sub BeginSeeking(source As UIElement, direction As Integer)
        If Not engine.IsLoaded Then Return
        seekDirection = direction
        isSeeking = True
        source.CaptureMouse()
        sfx.StartLoop("seek_whir.wav")
        engine.Pause()
        SpinWheelsSeeking(seekDirection)
        SeekTimer_Tick(Nothing, EventArgs.Empty)
        If isSeeking Then seekTimer.Start()
    End Sub

    Private Sub SeekButton_MouseUp(sender As Object, e As MouseEventArgs)
        EndSeeking(TryCast(sender, UIElement))
    End Sub

    ''' <summary>Safety net: if mouse capture is lost some other way (Alt+Tab, a system dialog)
    ''' while a seek button is held, still stop seeking instead of winding the tape forever.</summary>
    Private Sub SeekButton_LostMouseCapture(sender As Object, e As MouseEventArgs)
        EndSeeking(Nothing)
    End Sub

    Private Sub EndSeeking(source As UIElement)
        source?.ReleaseMouseCapture()
        sfx.StopLoop()
        If Not isSeeking Then Return
        isSeeking = False
        seekTimer.Stop()
        If Not engine.IsLoaded Then Return

        If isPaused Then
            ' Seeking while paused should land back in the paused state, not silently resume -
            ' just re-freeze the wheel at its new spot rather than restarting real playback.
            StopWheels()
        Else
            engine.Play()
            ResumeWheels()
        End If
    End Sub

    Private Sub SeekTimer_Tick(sender As Object, e As EventArgs)
        If Not engine.IsLoaded Then Return

        Dim newPosition = engine.Position + TimeSpan.FromSeconds(seekDirection)
        If newPosition < TimeSpan.Zero Then newPosition = TimeSpan.Zero
        If newPosition > songLength Then newPosition = songLength
        engine.Position = newPosition
        UpdateStretchForPosition(newPosition)

        Dim label = If(seekDirection < 0, "Rewinding", "Fast-forwarding")
        StatusText.Text = $"{label}: {newPosition:mm\:ss} / {songLength:mm\:ss}"

        If newPosition >= songLength Then
            isSeeking = False
            seekTimer.Stop()
            StopPlayback()
        End If
    End Sub

    ' ---- Record (system audio -> wav, via NAudio's WASAPI loopback capture) -----
    ' Loopback taps whatever the default output device is actually playing - this app's own
    ' tape playback, another app, anything mixed by Windows - rather than a microphone, so
    ' unlike the old mic-recording behavior, playback is deliberately left running instead of
    ' being stopped when Record is pressed: stopping it would defeat "record what's playing".

    Private Sub RecordButton_Click(sender As Object, e As RoutedEventArgs)
        If isRecording Then
            StopRecording()
        Else
            StartRecording()
        End If
    End Sub

    Private Sub StartRecording()
        Try
            sfx.Play("record_clunk.wav")

            Dim fileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
            recordingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)

            loopbackCapture = New WasapiLoopbackCapture()
            ' The loopback format (sample rate/channels/bit depth) is fixed by the system's
            ' current output mix format, not something we get to choose - read it back rather
            ' than assuming 44.1kHz mono the way the old mic setup did.
            waveWriter = New WaveFileWriter(recordingPath, loopbackCapture.WaveFormat)
            AddHandler loopbackCapture.DataAvailable, AddressOf LoopbackCapture_DataAvailable
            AddHandler loopbackCapture.RecordingStopped, AddressOf LoopbackCapture_RecordingStopped
            loopbackCapture.StartRecording()

            isRecording = True
            RecordButton.Content = "⏹"
            StatusText.Text = "Recording system audio..."
            SpinWheels()
        Catch ex As Exception
            StatusText.Text = "Recording failed: " & ex.Message
        End Try
    End Sub

    Private Sub LoopbackCapture_DataAvailable(sender As Object, e As WaveInEventArgs)
        waveWriter?.Write(e.Buffer, 0, e.BytesRecorded)
        ' Patches the RIFF/data chunk sizes in the header now rather than waiting for Dispose(),
        ' so the file on disk is always a valid, playable WAV even if the app is closed or
        ' crashes mid-recording instead of Stop being pressed.
        waveWriter?.Flush()
    End Sub

    Private Sub StopRecording()
        sfx.Play("release.wav")
        loopbackCapture?.StopRecording()
    End Sub

    Private Sub LoopbackCapture_RecordingStopped(sender As Object, e As StoppedEventArgs)
        waveWriter?.Dispose()
        waveWriter = Nothing
        loopbackCapture?.Dispose()
        loopbackCapture = Nothing
        isRecording = False

        Dispatcher.Invoke(Sub()
                              RecordButton.Content = "⏺"
                              StatusText.Text = "Saved: " & Path.GetFileName(recordingPath)
                              ' Recording no longer force-stops playback, so only freeze the wheels
                              ' here if a song isn't the reason they're still turning.
                              If Not positionTimer.IsEnabled Then StopWheels()
                              LoadPlaylist()
                          End Sub)
    End Sub

End Class

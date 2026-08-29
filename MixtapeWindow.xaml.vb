Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks

''' <summary>Lets the user assemble a run of songs — picked from individual files, or whole
''' folders (recursing into subfolders) — and bake them into a 60- or 90-minute mixtape, with an
''' optional text label shown in a handwritten-style font of their choice, like writing on a
''' cassette's paper label. Left unlabeled, it overwrites the classic 60minTape.mp3/90minTape.mp3
''' "blank tape"; given a label, it saves as a new file under the Mixtapes folder instead (see
''' MixtapeBuilder.ResolveOutputPath) with the label and font embedded in the MP3 itself (see
''' MixtapeBuilder.WriteLabel), discoverable later via Load Mixtape. Raises
''' <see cref="MixtapeBuilt"/> so Cassette can refresh its playlist once a tape is written.
''' Self-contained: unlike Cassette/ControlsWindow/PlaylistWindow, this window owns its own logic
''' rather than delegating to Cassette, since building a mixtape is a separate concern from the
''' playback/recording code Cassette centralizes.</summary>
Class MixtapeWindow

    Private ReadOnly AudioExtensions As String() = {"*.mp3", "*.wav", "*.flac"}

    ' A real mixtape side holds roughly this many songs; enforced as a hard cap so the list
    ' (and the build) stay a sane size, with a soft nudge below 10.
    Private Const MinRecommendedSongs As Integer = 10
    Private Const MaxSongs As Integer = 12

    Private Const DefaultFontOption As String = "(Default)"

    ' Windows/Office-bundled fonts that read as handwritten or script-style; filtered down at
    ' startup to whichever of these are actually installed, so the picker never offers a font
    ' that would silently fall back to a substitute.
    Private ReadOnly CandidateScriptFonts As String() = {
        "Segoe Script", "Segoe Print", "Monotype Corsiva", "Lucida Handwriting",
        "Bradley Hand ITC", "Kristen ITC", "Freestyle Script", "French Script MT",
        "Brush Script MT", "Vivaldi", "Vladimir Script", "Rage Italic", "Comic Sans MS"
    }

    Private ReadOnly songPaths As New List(Of String)
    Private isBuilding As Boolean

    Public Event MixtapeBuilt(outputPath As String)

    Public Sub New()
        InitializeComponent()
        AddHandler AddFilesButton.Click, AddressOf AddFilesButton_Click
        AddHandler AddFolderButton.Click, AddressOf AddFolderButton_Click
        AddHandler MoveUpButton.Click, AddressOf MoveUpButton_Click
        AddHandler MoveDownButton.Click, AddressOf MoveDownButton_Click
        AddHandler RemoveButton.Click, AddressOf RemoveButton_Click
        AddHandler ClearButton.Click, AddressOf ClearButton_Click
        AddHandler Build60Button.Click, AddressOf Build60Button_Click
        AddHandler Build90Button.Click, AddressOf Build90Button_Click
        AddHandler LabelTextBox.TextChanged, AddressOf UpdateLabelPreview
        AddHandler LabelFontCombo.SelectionChanged, AddressOf UpdateLabelPreview
        PopulateFontCombo()
        RefreshSummary()
    End Sub

    ' ---- Label + handwritten-font preview ---------------------------------

    ''' <summary>Offers "(Default)" plus every candidate script font that's actually installed on
    ''' this machine (Segoe Script/Print always are; the rest usually come with Office).</summary>
    Private Sub PopulateFontCombo()
        Dim installed = New HashSet(Of String)(
            Fonts.SystemFontFamilies.Select(Function(f) f.Source),
            StringComparer.OrdinalIgnoreCase)

        LabelFontCombo.Items.Add(DefaultFontOption)
        For Each candidate In CandidateScriptFonts
            If installed.Contains(candidate) Then LabelFontCombo.Items.Add(candidate)
        Next
        LabelFontCombo.SelectedIndex = 0
    End Sub

    Private Function SelectedFontFamilyName() As String
        Dim selected = TryCast(LabelFontCombo.SelectedItem, String)
        Return If(selected Is Nothing OrElse selected = DefaultFontOption, Nothing, selected)
    End Function

    ''' <summary>Redraws the little cassette-label preview in the chosen font, live as the user
    ''' types or switches fonts.</summary>
    Private Sub UpdateLabelPreview(sender As Object, e As RoutedEventArgs)
        Dim text = LabelTextBox.Text
        LabelPreviewText.Text = If(String.IsNullOrWhiteSpace(text), "(no label)", text)

        Dim fontName = SelectedFontFamilyName()
        LabelPreviewText.FontFamily = New FontFamily(If(fontName, "Segoe UI"))
    End Sub

    ' ---- Building the song list ------------------------------------------

    Private Sub AddFilesButton_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New Microsoft.Win32.OpenFileDialog With {
            .Title = "Add Songs to Mixtape",
            .Filter = "Audio Files (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac|All Files (*.*)|*.*",
            .Multiselect = True,
            .CheckFileExists = True
        }
        If dialog.ShowDialog(Me) = True Then
            AddSongs(dialog.FileNames)
        End If
    End Sub

    Private Sub AddFolderButton_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New Microsoft.Win32.OpenFolderDialog With {
            .Title = "Add Folder to Mixtape (includes subfolders)"
        }
        If dialog.ShowDialog(Me) = True Then
            Dim files = AudioExtensions.
                SelectMany(Function(ext) Directory.GetFiles(dialog.FolderName, ext, SearchOption.AllDirectories)).
                OrderBy(Function(f) f)
            AddSongs(files)
        End If
    End Sub

    ''' <summary>Appends each new file to the list, in order, skipping duplicates already
    ''' present, stopping once <see cref="MaxSongs"/> is reached, and refusing anything that
    ''' would push the running total past <see cref="MixtapeBuilder.NinetyMinutes"/> - the
    ''' biggest tape this deck makes, so the list can never grow into something no build could
    ''' ever fit. Pops up a playful heads-up if that 90-minute cap actually turned something away.</summary>
    Private Sub AddSongs(paths As IEnumerable(Of String))
        Dim added = 0
        Dim skippedDuplicate = 0
        Dim skippedCap = 0
        Dim skippedFull = 0
        Dim runningTotal = CurrentTotalDuration()

        For Each path In paths
            If songPaths.Contains(path, StringComparer.OrdinalIgnoreCase) Then
                skippedDuplicate += 1
            ElseIf songPaths.Count >= MaxSongs Then
                skippedCap += 1
            Else
                Dim projectedTotal = runningTotal + If(TryGetDuration(path), TimeSpan.Zero)
                If projectedTotal > MixtapeBuilder.NinetyMinutes Then
                    skippedFull += 1
                Else
                    songPaths.Add(path)
                    runningTotal = projectedTotal
                    added += 1
                End If
            End If
        Next

        RefreshSongsList()

        Dim notes As New List(Of String)
        If skippedDuplicate > 0 Then notes.Add($"{skippedDuplicate} already in the list")
        If skippedCap > 0 Then notes.Add($"{skippedCap} skipped (12-song limit reached)")
        If skippedFull > 0 Then notes.Add($"{skippedFull} skipped (tape's full at 90 min)")
        StatusText.Text = If(notes.Count > 0,
            $"Added {added} song(s). " & String.Join("; ", notes) & ".",
            $"Added {added} song(s).")

        If skippedFull > 0 Then ShowTapeFullMessage(skippedFull)
    End Sub

    ''' <summary>The fun version of "your selection is too long": a playful heads-up instead of a
    ''' dry validation error, shown whenever adding songs actually hit the 90-minute hard cap.</summary>
    Private Sub ShowTapeFullMessage(skippedCount As Integer)
        Dim songWord = If(skippedCount = 1, "song", "songs")
        MessageBox.Show(Me,
            $"Whoa, hold the tape! 🎉📼 Adding {skippedCount} more {songWord} would run right off the end of the reel." & vbCrLf & vbCrLf &
            "This mixtape's maxed out at 90 minutes - the biggest tape this deck makes. Save this batch and start a B-side! 🔁",
            "Tape's Full!", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    Private Sub MoveUpButton_Click(sender As Object, e As RoutedEventArgs)
        MoveSelected(-1)
    End Sub

    Private Sub MoveDownButton_Click(sender As Object, e As RoutedEventArgs)
        MoveSelected(1)
    End Sub

    Private Sub MoveSelected(direction As Integer)
        Dim index = SongsList.SelectedIndex
        Dim newIndex = index + direction
        If index < 0 OrElse newIndex < 0 OrElse newIndex >= songPaths.Count Then Return

        Dim item = songPaths(index)
        songPaths.RemoveAt(index)
        songPaths.Insert(newIndex, item)
        RefreshSongsList()
        SongsList.SelectedIndex = newIndex
    End Sub

    Private Sub RemoveButton_Click(sender As Object, e As RoutedEventArgs)
        Dim index = SongsList.SelectedIndex
        If index < 0 Then Return
        songPaths.RemoveAt(index)
        RefreshSongsList()
    End Sub

    Private Sub ClearButton_Click(sender As Object, e As RoutedEventArgs)
        songPaths.Clear()
        RefreshSongsList()
    End Sub

    ''' <summary>Redraws the list from <see cref="songPaths"/>, one line per song with its
    ''' position and duration, and refreshes the running-total summary.</summary>
    Private Sub RefreshSongsList()
        SongsList.Items.Clear()
        For i = 0 To songPaths.Count - 1
            Dim songPath = songPaths(i)
            Dim duration = TryGetDuration(songPath)
            Dim label = $"{i + 1:00}. {Path.GetFileName(songPath)}"
            If duration.HasValue Then label &= $"  ({duration.Value:mm\:ss})"
            SongsList.Items.Add(New ListBoxItem With {.Content = label, .Tag = songPath})
        Next
        RefreshSummary()
    End Sub

    Private Function TryGetDuration(path As String) As TimeSpan?
        Try
            Return MixtapeBuilder.GetDuration(path)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function CurrentTotalDuration() As TimeSpan
        Dim total = TimeSpan.Zero
        For Each path In songPaths
            Dim d = TryGetDuration(path)
            If d.HasValue Then total += d.Value
        Next
        Return total
    End Function

    ''' <summary>Redraws the running total and its "fuel gauge" color/quip (calm white under 60
    ''' minutes, amber past it, red once it's brushing the 90-minute hard cap), and enables each
    ''' Build button only if the current total would actually fit that tape - no more silent
    ''' trimming, the button just won't let you try.</summary>
    Private Sub RefreshSummary()
        Dim total = CurrentTotalDuration()

        Dim countNote = If(songPaths.Count < MinRecommendedSongs, " (10-12 recommended)",
                        If(songPaths.Count > MaxSongs, " (over the 12-song limit)", ""))

        Dim capacityNote As String
        Dim capacityColor As Brush
        If total >= MixtapeBuilder.NinetyMinutes Then
            capacityNote = "  🚫 TAPE'S FULL (90-min max)!"
            capacityColor = Brushes.OrangeRed
        ElseIf total > MixtapeBuilder.SixtyMinutes Then
            capacityNote = "  ⏳ Past 60 min — only the 90-min tape fits this now"
            capacityColor = New SolidColorBrush(Color.FromRgb(224, 160, 48))
        Else
            capacityNote = ""
            capacityColor = Brushes.White
        End If

        SummaryText.Text = $"{songPaths.Count} song(s) selected{countNote} — total {total:hh\:mm\:ss}{capacityNote}"
        SummaryText.Foreground = capacityColor

        Dim canBuildAtAll = songPaths.Count > 0 AndAlso Not isBuilding
        Build60Button.IsEnabled = canBuildAtAll AndAlso total <= MixtapeBuilder.SixtyMinutes
        Build90Button.IsEnabled = canBuildAtAll AndAlso total <= MixtapeBuilder.NinetyMinutes
        Build60Button.ToolTip = If(Build60Button.IsEnabled, Nothing, "Your selection runs past 60 minutes — trim it or use the 90-min tape")
    End Sub

    ' ---- Building the tape ------------------------------------------------

    Private Sub Build60Button_Click(sender As Object, e As RoutedEventArgs)
        BuildMixtape("60minTape.mp3", MixtapeBuilder.SixtyMinutes)
    End Sub

    Private Sub Build90Button_Click(sender As Object, e As RoutedEventArgs)
        BuildMixtape("90minTape.mp3", MixtapeBuilder.NinetyMinutes)
    End Sub

    ''' <summary>Decodes/concatenates/encodes off the UI thread and reports the result. Build60Button
    ''' is only enabled when the total already fits 60 minutes (and songs can never be added past
    ''' the 90-minute hard cap - see AddSongs), so this shouldn't ever need to trim; MixtapeBuilder.Build
    ''' still clamps to <paramref name="capacity"/> regardless, as a defensive fallback. A blank name
    ''' overwrites the default <paramref name="defaultFileName"/> tape; a real name is saved as a new
    ''' file under the Mixtapes folder instead (see MixtapeBuilder.ResolveOutputPath).</summary>
    Private Async Sub BuildMixtape(defaultFileName As String, capacity As TimeSpan)
        If songPaths.Count = 0 OrElse isBuilding Then Return

        Dim outputPath = MixtapeBuilder.ResolveOutputPath(LabelTextBox.Text, defaultFileName)
        Dim displayName = Path.GetFileName(outputPath)
        Dim paths = songPaths.ToList()
        Dim label = LabelTextBox.Text
        Dim fontFamily = SelectedFontFamilyName()

        isBuilding = True
        RefreshSummary()
        StatusText.Text = $"Building {displayName}..."

        Try
            Await Task.Run(Sub() MixtapeBuilder.Build(paths, outputPath, capacity, label, fontFamily))
            StatusText.Text = $"Saved {displayName} ({MixtapeBuilder.GetDuration(outputPath):hh\:mm\:ss})."
            RaiseEvent MixtapeBuilt(outputPath)
        Catch ex As Exception
            StatusText.Text = $"Failed to build {displayName}: {ex.Message}"
        Finally
            isBuilding = False
            RefreshSummary()
        End Try
    End Sub

End Class

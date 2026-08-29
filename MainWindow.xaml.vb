Imports System.ComponentModel
Imports System.Windows.Shell

''' <summary>App shell: one real top-level Window hosting every panel (Cassette, Controls, Album
''' Art, Spectrum, Visualizer, Graphic Equalizer, Playlist, and the cosmetic extras) as its own
''' dockable/floatable/closable AvalonDock pane. Cassette still creates and owns every other panel
''' and holds all the playback/recording/animation logic exactly as before; this window's only job
''' is placing each of Cassette's panels into its own LayoutAnchorable and cleaning up on exit.</summary>
Class MainWindow

    Private ReadOnly cassette As New Cassette()

    Public Sub New()
        InitializeComponent()

        CassettePane.Content = cassette
        ControlsPane.Content = cassette.ControlsView
        AlbumArtPane.Content = cassette.AlbumArtView
        EqualizerPane.Content = cassette.EqualizerView
        VisualizerPane.Content = cassette.VisualizerView
        EqualizerControlsPane.Content = cassette.EqualizerControlsView
        PlaylistPane.Content = cassette.PlaylistView

        ThemePane.Content = cassette.ThemeView
        CreditsPane.Content = cassette.CreditsView
        LinerNotesPane.Content = cassette.LinerNotesView
        VuMetersPane.Content = cassette.VuMetersView
        TapeCounterPane.Content = cassette.TapeCounterView
        StickersPane.Content = cassette.StickersView
        ConcertPosterPane.Content = cassette.ConcertPosterView
        RetroAdPane.Content = cassette.RetroAdView
        StaticNoisePane.Content = cassette.StaticNoiseView

        AddHandler Me.StateChanged, AddressOf MainWindow_StateChanged
    End Sub

    ' ---- Custom title bar (WindowChrome removed the native one) ---------

    Private Sub MinimizeButton_Click(sender As Object, e As RoutedEventArgs)
        SystemCommands.MinimizeWindow(Me)
    End Sub

    Private Sub MaximizeRestoreButton_Click(sender As Object, e As RoutedEventArgs)
        If WindowState = WindowState.Maximized Then
            SystemCommands.RestoreWindow(Me)
        Else
            SystemCommands.MaximizeWindow(Me)
        End If
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As RoutedEventArgs)
        SystemCommands.CloseWindow(Me)
    End Sub

    ''' <summary>Swaps the maximize-button glyph for a "restore" (overlapping squares) glyph while
    ''' maximized - covers both the button click above and maximizing another way (double-clicking
    ''' the title bar, dragging to the top edge, the system menu).</summary>
    Private Sub MainWindow_StateChanged(sender As Object, e As EventArgs)
        Dim isMaximized = WindowState = WindowState.Maximized
        MaximizeGlyph.Visibility = If(isMaximized, Visibility.Collapsed, Visibility.Visible)
        RestoreGlyph.Visibility = If(isMaximized, Visibility.Visible, Visibility.Collapsed)
        MaximizeRestoreButton.ToolTip = If(isMaximized, "Restore Down", "Maximize")
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs)
        cassette.Cleanup()
    End Sub

End Class

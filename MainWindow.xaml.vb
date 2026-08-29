Imports System.ComponentModel
Imports System.Windows.Shell

''' <summary>App shell: one fixed, non-dockable layout (library sidebar, cassette centerpiece, a
''' tabbed side panel, a persistent bottom transport bar) instead of freely-arrangeable panes -
''' see MainWindow.xaml for why. Cassette still creates and owns every other panel and holds all
''' the playback/recording/animation logic exactly as before; this window's only job is slotting
''' each of Cassette's panels into its ContentControl host and cleaning up on exit.</summary>
Class MainWindow

    Private ReadOnly cassette As New Cassette()

    ' Album Art's optional maximize/detach - Nothing/False means it's sitting in its normal
    ' AlbumArtHost slot. Only one of the two states applies at a time; both handlers below no-op
    ' if the other one is already active rather than trying to untangle both at once.
    Private albumArtMaximized As Boolean
    Private albumArtDetachedWindow As Window

    Public Sub New()
        InitializeComponent()

        CassetteHost.Content = cassette
        ControlsHost.Content = cassette.ControlsView
        AlbumArtHost.Content = cassette.AlbumArtView
        EqualizerHost.Content = cassette.EqualizerView
        VisualizerHost.Content = cassette.VisualizerView
        EqualizerControlsHost.Content = cassette.EqualizerControlsView
        PlaylistHost.Content = cassette.PlaylistView

        ThemeHost.Content = cassette.ThemeView
        CreditsHost.Content = cassette.CreditsView
        LinerNotesHost.Content = cassette.LinerNotesView
        VuMetersHost.Content = cassette.VuMetersView
        TapeCounterHost.Content = cassette.TapeCounterView
        StickersHost.Content = cassette.StickersView
        ConcertPosterHost.Content = cassette.ConcertPosterView
        RetroAdHost.Content = cassette.RetroAdView
        StaticNoiseHost.Content = cassette.StaticNoiseView

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

    ' ---- Album Art: optional maximize / detach ---------------------------

    Private Sub AlbumArtMaximizeButton_Click(sender As Object, e As RoutedEventArgs)
        If albumArtDetachedWindow IsNot Nothing OrElse albumArtMaximized Then Return

        AlbumArtHost.Content = Nothing
        MaximizeOverlayHost.Content = cassette.AlbumArtView
        MaximizeOverlay.Visibility = Visibility.Visible
        albumArtMaximized = True

        ' Collapse Album Art's own column (and the splitter right before it) to zero width while
        ' maximized, instead of just covering it with the overlay - otherwise the empty column
        ' still shows through along the overlay's edges and the rest of the layout doesn't reflow
        ' to use that space.
        CollapseColumn(AlbumArtColumn)
        CollapseColumn(AlbumArtSplitterColumn)
    End Sub

    Private Sub MaximizeOverlayCloseButton_Click(sender As Object, e As RoutedEventArgs)
        MaximizeOverlay.Visibility = Visibility.Collapsed
        MaximizeOverlayHost.Content = Nothing
        AlbumArtHost.Content = cassette.AlbumArtView
        albumArtMaximized = False

        RestoreColumn(AlbumArtColumn)
        RestoreColumn(AlbumArtSplitterColumn)
    End Sub

    ' GridSplitter drags leave MinWidth/Width as ordinary numbers (not "*"), so collapsing just
    ' means remembering both and zeroing them out - MinWidth alone would still force a floor.
    Private ReadOnly collapsedColumnWidths As New Dictionary(Of ColumnDefinition, (Width As GridLength, MinWidth As Double))

    Private Sub CollapseColumn(column As ColumnDefinition)
        collapsedColumnWidths(column) = (column.Width, column.MinWidth)
        column.MinWidth = 0
        column.Width = New GridLength(0)
    End Sub

    Private Sub RestoreColumn(column As ColumnDefinition)
        If Not collapsedColumnWidths.ContainsKey(column) Then Return
        Dim saved = collapsedColumnWidths(column)
        column.Width = saved.Width
        column.MinWidth = saved.MinWidth
        collapsedColumnWidths.Remove(column)
    End Sub

    ''' <summary>First click pops Album Art out into its own resizable window; a second click (or
    ''' just closing that window normally) redocks it back into its usual slot.</summary>
    Private Sub AlbumArtDetachButton_Click(sender As Object, e As RoutedEventArgs)
        If albumArtMaximized Then Return

        If albumArtDetachedWindow IsNot Nothing Then
            albumArtDetachedWindow.Close()
            Return
        End If

        AlbumArtHost.Content = Nothing
        Dim floatingWindow As New Window With {
            .Title = "Album Art",
            .Owner = Me,
            .Width = 340, .Height = 380, .MinWidth = 200, .MinHeight = 200,
            .Content = cassette.AlbumArtView,
            .WindowStartupLocation = WindowStartupLocation.CenterOwner
        }
        floatingWindow.SetResourceReference(Window.BackgroundProperty, "Bg0Brush")
        AddHandler floatingWindow.Closed, AddressOf AlbumArtDetachedWindow_Closed
        albumArtDetachedWindow = floatingWindow
        floatingWindow.Show()
    End Sub

    Private Sub AlbumArtDetachedWindow_Closed(sender As Object, e As EventArgs)
        albumArtDetachedWindow = Nothing
        AlbumArtHost.Content = cassette.AlbumArtView
    End Sub

End Class

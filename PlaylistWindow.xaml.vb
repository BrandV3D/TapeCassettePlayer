''' <summary>Companion window that just shows the playlist; Cassette owns it and gives it an
''' initial position, but it's independently movable and resizable like Cassette and Controls.</summary>
Class PlaylistWindow

    ''' <summary>Raised when the user clicks Refresh; Cassette owns the actual folder-scanning
    ''' logic (LoadPlaylist), so this window just reports the request.</summary>
    Public Event RefreshRequested()

    Public Sub New()
        InitializeComponent()
        AddHandler RefreshButton.Click, AddressOf RefreshButton_Click
    End Sub

    Private Sub RefreshButton_Click(sender As Object, e As RoutedEventArgs)
        RaiseEvent RefreshRequested()
    End Sub

End Class

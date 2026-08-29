Imports System.Windows.Media.Imaging

''' <summary>Companion window that shows the currently playing song's embedded cover art. Cassette
''' owns it (alongside ControlsWindow/PlaylistWindow) and calls <see cref="SetArt"/> whenever a new
''' song starts, passing whatever AlbumArtReader found (or Nothing for the placeholder).</summary>
Class AlbumArtWindow

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub SetArt(art As BitmapImage)
        ArtImage.Source = art
        PlaceholderText.Visibility = If(art Is Nothing, Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class

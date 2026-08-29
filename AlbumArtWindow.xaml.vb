Imports System.Windows.Media.Imaging

''' <summary>Companion window that shows the currently playing song's embedded cover art. Cassette
''' owns it (alongside ControlsWindow/PlaylistWindow) and calls <see cref="SetArt"/> whenever a new
''' song starts, passing whatever AlbumArtReader found (or Nothing to fall back to the stand-in
''' cover below, for a song with no embedded art of its own).</summary>
Class AlbumArtWindow

    Private Shared ReadOnly DefaultArt As New BitmapImage(New Uri("pack://application:,,,/albumart.jpg"))

    Public Sub New()
        InitializeComponent()
        ArtImage.Source = DefaultArt
    End Sub

    Public Sub SetArt(art As BitmapImage)
        ArtImage.Source = If(art, DefaultArt)
    End Sub

End Class

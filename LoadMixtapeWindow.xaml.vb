Imports System.IO
Imports System.Linq

''' <summary>Lists mixtapes previously built by MixtapeWindow — named ones from the Mixtapes
''' subfolder, plus the two default quick tapes (60minTape.mp3/90minTape.mp3) if they exist —
''' newest first, each shown by its embedded label (in whatever handwritten font it was labeled
''' with) rather than its raw filename when one was set. Reports the chosen one back via
''' <see cref="MixtapeSelected"/> so Cassette can load and play it.</summary>
Class LoadMixtapeWindow

    Public Event MixtapeSelected(path As String)

    Public Sub New()
        InitializeComponent()
        AddHandler RefreshButton.Click, AddressOf RefreshButton_Click
        AddHandler LoadButton.Click, AddressOf LoadButton_Click
        AddHandler CancelButton.Click, AddressOf CancelButton_Click
        AddHandler MixtapesList.MouseDoubleClick, AddressOf MixtapesList_MouseDoubleClick
        RefreshList()
    End Sub

    Private Sub RefreshButton_Click(sender As Object, e As RoutedEventArgs)
        RefreshList()
    End Sub

    Private Shared ReadOnly MixtapeExtensions As String() = {"*.mp3", "*.flac", "*.wav"}

    ''' <summary>Scans for every mixtape on disk, in any supported format — named ones under the
    ''' Mixtapes subfolder, plus the default quick tapes (60minTape/90minTape, per format - see
    ''' MixtapeBuilder.ResolveOutputPath) at the app's base directory if present — newest first.</summary>
    Private Sub RefreshList()
        MixtapesList.Items.Clear()

        Dim baseDir = AppDomain.CurrentDomain.BaseDirectory
        Dim defaultTapes = {"60minTape", "90minTape"}.
            SelectMany(Function(baseName) MixtapeExtensions.Select(Function(ext) baseName & ext.TrimStart("*"c))).
            Select(Function(name) Path.Combine(baseDir, name)).
            Where(Function(p) File.Exists(p))

        Dim mixtapesDir = MixtapeBuilder.GetMixtapesDirectory()
        Dim namedTapes = MixtapeExtensions.SelectMany(Function(ext) Directory.GetFiles(mixtapesDir, ext))

        Dim allTapes = defaultTapes.Concat(namedTapes).
            OrderByDescending(Function(p) File.GetLastWriteTime(p)).
            ToList()

        For Each tapePath In allTapes
            MixtapesList.Items.Add(New ListBoxItem With {.Content = BuildEntryContent(tapePath), .Tag = tapePath})
        Next

        If allTapes.Count = 0 Then
            MixtapesList.Items.Add(New ListBoxItem With {.Content = "No mixtapes yet — build one with Make Mixtape.", .IsEnabled = False})
        Else
            MixtapesList.SelectedIndex = 0
        End If
    End Sub

    ''' <summary>Builds one list entry: the mixtape's embedded label (or its filename if unlabeled),
    ''' rendered in whatever handwritten font it was labeled with, plus duration/modified-date detail.</summary>
    Private Function BuildEntryContent(tapePath As String) As UIElement
        Dim tags = MixtapeBuilder.ReadLabel(tapePath)
        Dim displayName = If(String.IsNullOrWhiteSpace(tags.Label), Path.GetFileNameWithoutExtension(tapePath), tags.Label)

        Dim modified = File.GetLastWriteTime(tapePath)
        Dim durationText = ""
        Try
            durationText = $"({MixtapeBuilder.GetDuration(tapePath):hh\:mm\:ss})"
        Catch
        End Try

        Dim panel As New StackPanel With {.Orientation = Orientation.Horizontal}
        panel.Children.Add(New TextBlock With {
            .Text = displayName,
            .FontFamily = New FontFamily(If(String.IsNullOrWhiteSpace(tags.FontFamily), "Segoe UI", tags.FontFamily)),
            .FontSize = 16,
            .VerticalAlignment = VerticalAlignment.Center
        })
        Dim formatTag = Path.GetExtension(tapePath).TrimStart("."c).ToUpperInvariant()
        panel.Children.Add(New TextBlock With {
            .Text = $"  {durationText}  —  {formatTag}  —  {modified:g}",
            .FontSize = 11,
            .Foreground = Brushes.Gray,
            .VerticalAlignment = VerticalAlignment.Center
        })
        Return panel
    End Function

    Private Sub MixtapesList_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        LoadSelected()
    End Sub

    Private Sub LoadButton_Click(sender As Object, e As RoutedEventArgs)
        LoadSelected()
    End Sub

    Private Sub LoadSelected()
        Dim selected = TryCast(MixtapesList.SelectedItem, ListBoxItem)
        Dim tapePath = TryCast(selected?.Tag, String)
        If tapePath Is Nothing Then Return
        RaiseEvent MixtapeSelected(tapePath)
        Close()
    End Sub

    Private Sub CancelButton_Click(sender As Object, e As RoutedEventArgs)
        Close()
    End Sub

End Class

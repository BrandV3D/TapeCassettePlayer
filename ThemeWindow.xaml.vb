''' <summary>Theme picker: four swatches preview and live-apply Dark/Light/Blue/Green (see
''' Themes/*.xaml and ThemeManager.vb), and Save/Load make that choice persist across launches.
''' Picking a swatch only previews it - Save is what writes it to disk, and Load reverts to
''' whatever was last saved, discarding an unsaved preview.</summary>
Class ThemeWindow

    Private suppressSwatchEvents As Boolean

    Public Sub New()
        InitializeComponent()

        AddHandler DarkSwatch.Checked, Sub() ApplyPreview(ThemeManager.AppTheme.Dark)
        AddHandler LightSwatch.Checked, Sub() ApplyPreview(ThemeManager.AppTheme.Light)
        AddHandler BlueSwatch.Checked, Sub() ApplyPreview(ThemeManager.AppTheme.Blue)
        AddHandler GreenSwatch.Checked, Sub() ApplyPreview(ThemeManager.AppTheme.Green)
        AddHandler GraffitiSwatch.Checked, Sub() ApplyPreview(ThemeManager.AppTheme.Graffiti)
        AddHandler SaveButton.Click, AddressOf SaveButton_Click
        AddHandler LoadButton.Click, AddressOf LoadButton_Click

        SelectSwatch(ThemeManager.CurrentTheme)
    End Sub

    Private Sub ApplyPreview(theme As ThemeManager.AppTheme)
        If suppressSwatchEvents Then Return
        ThemeManager.ApplyTheme(theme)
        StatusText.Text = $"Previewing {theme} - click Save to keep it"
    End Sub

    Private Sub SaveButton_Click(sender As Object, e As RoutedEventArgs)
        ThemeManager.SaveTheme(ThemeManager.CurrentTheme)
        StatusText.Text = $"Saved {ThemeManager.CurrentTheme} - it'll load automatically next time"
    End Sub

    Private Sub LoadButton_Click(sender As Object, e As RoutedEventArgs)
        Dim saved = ThemeManager.LoadSavedTheme()
        ThemeManager.ApplyTheme(saved)
        SelectSwatch(saved)
        StatusText.Text = $"Loaded the saved theme ({saved})"
    End Sub

    ''' <summary>Checks the matching RadioButton without re-triggering ApplyPreview - used when
    ''' Load reverts the theme out from under whatever swatch was showing as selected.</summary>
    Private Sub SelectSwatch(theme As ThemeManager.AppTheme)
        suppressSwatchEvents = True
        Select Case theme
            Case ThemeManager.AppTheme.Dark : DarkSwatch.IsChecked = True
            Case ThemeManager.AppTheme.Light : LightSwatch.IsChecked = True
            Case ThemeManager.AppTheme.Blue : BlueSwatch.IsChecked = True
            Case ThemeManager.AppTheme.Green : GreenSwatch.IsChecked = True
            Case ThemeManager.AppTheme.Graffiti : GraffitiSwatch.IsChecked = True
        End Select
        suppressSwatchEvents = False
    End Sub

End Class

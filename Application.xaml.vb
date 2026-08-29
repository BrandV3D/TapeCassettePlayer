Class Application

    ''' <summary>Applies the last-saved (or default Dark) theme before MainWindow gets created by
    ''' StartupUri, which happens inside the MyBase.OnStartup call below - so every window's very
    ''' first resource lookup already resolves against the right theme, with no flash of the wrong
    ''' skin.</summary>
    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        ThemeManager.ApplyTheme(ThemeManager.LoadSavedTheme())
        MyBase.OnStartup(e)
    End Sub

End Class

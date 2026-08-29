Imports System.IO

''' <summary>Swaps the app's whole skin at runtime by loading one of the four Themes/*.xaml
''' ResourceDictionaries into Application.Resources.MergedDictionaries in place of whichever one is
''' currently merged in - every Background/Foreground/Brush in the app that's set via
''' {DynamicResource SomeKey} (instead of a hardcoded color or {StaticResource ...}) picks up the
''' new values immediately, with no restart and no per-window code. Also persists the choice to a
''' small settings file so it's restored on the next launch.</summary>
Public Module ThemeManager

    Public Enum AppTheme
        Dark
        Light
        Blue
        Green
        Graffiti
    End Enum

    ''' <summary>Raised after ApplyTheme finishes swapping the dictionary, so anything that builds
    ''' its own brushes in code (the Equalizer's bar gradient, the Visualizer's line/fill gradients,
    ''' the grid-line color, etc. - all built once in a constructor rather than bound via XAML) knows
    ''' to rebuild them from the new theme's colors.</summary>
    Public Event ThemeChanged As EventHandler

    Public Property CurrentTheme As AppTheme = AppTheme.Dark

    Private ReadOnly SettingsPath As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TapeCassettePlayer", "theme.txt")

    Private currentDictionary As ResourceDictionary

    ''' <summary>Applies (but does not save) a theme - lets the theme picker preview a skin live
    ''' before the user commits to it with Save.</summary>
    Public Sub ApplyTheme(theme As AppTheme)
        Dim uri As New Uri($"pack://application:,,,/Themes/{theme}Theme.xaml")
        Dim newDictionary As New ResourceDictionary With {.Source = uri}

        Dim mergedDictionaries = Application.Current.Resources.MergedDictionaries
        If currentDictionary IsNot Nothing Then mergedDictionaries.Remove(currentDictionary)
        mergedDictionaries.Add(newDictionary)
        currentDictionary = newDictionary

        CurrentTheme = theme
        RaiseEvent ThemeChanged(Nothing, EventArgs.Empty)
    End Sub

    Public Sub SaveTheme(theme As AppTheme)
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath))
            File.WriteAllText(SettingsPath, theme.ToString())
        Catch
            ' Best-effort - a failed save just means the next launch falls back to Dark.
        End Try
    End Sub

    Public Function LoadSavedTheme() As AppTheme
        Try
            If File.Exists(SettingsPath) Then
                Dim saved As AppTheme
                If [Enum].TryParse(File.ReadAllText(SettingsPath).Trim(), saved) Then Return saved
            End If
        Catch
        End Try
        Return AppTheme.Dark
    End Function

End Module

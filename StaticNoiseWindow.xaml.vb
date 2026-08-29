Imports System.Windows.Media.Animation

''' <summary>Purely cosmetic - scanline texture plus a slow flicker on the headline, evoking a dead
''' channel / tape hiss. No real signal to display, which is rather the point.</summary>
Class StaticNoiseWindow

    Public Sub New()
        InitializeComponent()
        AddHandler ThemeManager.ThemeChanged, Sub() BuildScanlines()
        AddHandler Me.Loaded, Sub()
                                   BuildScanlines()
                                   StartFlicker()
                               End Sub
    End Sub

    Private Sub ScanlinesCanvas_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        BuildScanlines()
    End Sub

    Private Sub BuildScanlines()
        ScanlinesCanvas.Children.Clear()
        Dim w = ScanlinesCanvas.ActualWidth
        Dim h = ScanlinesCanvas.ActualHeight
        If w <= 0 OrElse h <= 0 Then Return

        Dim lineBrush As New SolidColorBrush(CType(Application.Current.Resources("GridLineColor"), Color))

        Dim y = 0.0
        While y < h
            Dim line As New Rectangle With {.Width = w, .Height = 1, .Fill = lineBrush}
            Canvas.SetTop(line, y)
            ScanlinesCanvas.Children.Add(line)
            y += 3
        End While
    End Sub

    Private Sub StartFlicker()
        Dim anim As New DoubleAnimation With {
            .From = 1, .To = 0.55, .Duration = New Duration(TimeSpan.FromSeconds(2.3)),
            .AutoReverse = True, .RepeatBehavior = RepeatBehavior.Forever
        }
        GlitchText.BeginAnimation(TextBlock.OpacityProperty, anim)
    End Sub

End Class

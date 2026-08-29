Imports System.Windows.Media.Animation

''' <summary>Purely cosmetic - the two needles idly sway on independent, slightly different cycles
''' (not driven by real audio) so the meters never feel perfectly synced/mechanical.</summary>
Class VuMetersWindow

    Public Sub New()
        InitializeComponent()
        AddHandler Me.Loaded, Sub() StartSway()
    End Sub

    Private Sub StartSway()
        Animate(LeftNeedleRotate, -30, 18, 1.7)
        Animate(RightNeedleRotate, -18, 26, 2.1)
    End Sub

    Private Sub Animate(target As RotateTransform, fromAngle As Double, toAngle As Double, seconds As Double)
        Dim anim As New DoubleAnimation With {
            .From = fromAngle,
            .To = toAngle,
            .Duration = New Duration(TimeSpan.FromSeconds(seconds)),
            .AutoReverse = True,
            .RepeatBehavior = RepeatBehavior.Forever,
            .EasingFunction = New SineEase With {.EasingMode = EasingMode.EaseInOut}
        }
        target.BeginAnimation(RotateTransform.AngleProperty, anim)
    End Sub

End Class

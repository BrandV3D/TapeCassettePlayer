Imports System.Windows.Threading

''' <summary>Companion window that shows an animated bar-style graphic equalizer. Cassette owns it
''' (alongside ControlsWindow/PlaylistWindow/AlbumArtWindow) and calls <see cref="SetPlaying"/>
''' whenever playback starts or stops. There's no real-time FFT of the audio (MediaElement doesn't
''' expose raw samples), so each bar is driven by a few layered sine waves at its own random speed
''' plus a little jitter - close enough to a real spectrum to look alive, and it settles flat the
''' moment playback stops.</summary>
Class EqualizerWindow

    Private Const BarCount As Integer = 26
    Private Const BarGapRatio As Double = 0.28 ' fraction of each bar's slot left as gap
    Private Const ReflectionHeightRatio As Double = 0.22
    Private Const ReflectionGap As Double = 6

    Private ReadOnly rng As New Random()
    Private ReadOnly renderTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(1000 / 45)}

    Private bars(BarCount - 1) As Rectangle
    Private reflections(BarCount - 1) As Rectangle
    Private peaks(BarCount - 1) As Rectangle

    ' Two sine oscillators per bar (different speed/phase) summed together for an organic,
    ' non-repeating wobble instead of a single obvious pulse.
    Private freqA(BarCount - 1) As Double
    Private freqB(BarCount - 1) As Double
    Private phaseA(BarCount - 1) As Double
    Private phaseB(BarCount - 1) As Double

    Private currentHeight(BarCount - 1) As Double ' smoothed, animated toward target each tick
    Private peakHeight(BarCount - 1) As Double
    Private peakVelocity(BarCount - 1) As Double

    Private elapsedSeconds As Double
    Private energy As Double ' 0 = idle/stopped (bars flat), 1 = fully playing
    Private targetEnergy As Double

    Private ReadOnly barBrush As LinearGradientBrush
    Private ReadOnly barStart As New GradientStop(Colors.Transparent, 0)
    Private ReadOnly barMid As New GradientStop(Colors.Transparent, 0.55)
    Private ReadOnly barEnd As New GradientStop(Colors.Transparent, 1)
    Private ReadOnly gridLineBrush As New SolidColorBrush()

    Public Sub New()
        InitializeComponent()

        barBrush = New LinearGradientBrush() With {.StartPoint = New Point(0, 1), .EndPoint = New Point(0, 0)}
        barBrush.GradientStops.Add(barStart)
        barBrush.GradientStops.Add(barMid)
        barBrush.GradientStops.Add(barEnd)
        RefreshThemeColors()

        For i = 0 To BarCount - 1
            freqA(i) = 0.6 + rng.NextDouble() * 1.4
            freqB(i) = 1.4 + rng.NextDouble() * 2.6
            phaseA(i) = rng.NextDouble() * Math.PI * 2
            phaseB(i) = rng.NextDouble() * Math.PI * 2
        Next

        BuildBars()
        AddHandler renderTimer.Tick, AddressOf RenderTimer_Tick
        AddHandler ThemeManager.ThemeChanged, AddressOf ThemeManager_ThemeChanged
        AddHandler Me.SizeChanged, Sub() BuildGridLines()
        AddHandler Me.Loaded, Sub()
                                   BuildGridLines()
                                   renderTimer.Start()
                               End Sub
        AddHandler Me.Unloaded, Sub()
                                     renderTimer.Stop()
                                     RemoveHandler ThemeManager.ThemeChanged, AddressOf ThemeManager_ThemeChanged
                                 End Sub
    End Sub

    ''' <summary>GradientStop/SolidColorBrush are Freezable, not FrameworkElement, so they have no
    ''' SetResourceReference - unlike a control's own Foreground/Fill, these have to be refreshed by
    ''' hand whenever ThemeManager swaps the theme dictionary.</summary>
    Private Sub ThemeManager_ThemeChanged(sender As Object, e As EventArgs)
        RefreshThemeColors()
    End Sub

    Private Sub RefreshThemeColors()
        Dim resources = Application.Current.Resources
        barStart.Color = CType(resources("AccentStartColor"), Color)
        barMid.Color = CType(resources("AccentMidColor"), Color)
        barEnd.Color = CType(resources("AccentEndColor"), Color)
        gridLineBrush.Color = CType(resources("GridLineColor"), Color)
    End Sub

    ''' <summary>Tells the equalizer whether audio is actually playing. Bars ease their energy
    ''' toward full life while playing and settle back down to a flat line when stopped, rather than
    ''' snapping instantly, so Play/Stop feels like part of the same animation instead of a toggle.</summary>
    Public Sub SetPlaying(isPlaying As Boolean)
        targetEnergy = If(isPlaying, 1.0, 0.0)
    End Sub

    Private Sub BuildBars()
        BarsCanvas.Children.Clear()
        For i = 0 To BarCount - 1
            Dim reflection As New Rectangle With {
                .Fill = barBrush,
                .RadiusX = 2, .RadiusY = 2,
                .Opacity = 0.28
            }
            Dim bar As New Rectangle With {
                .Fill = barBrush,
                .RadiusX = 2, .RadiusY = 2
            }
            Dim peak As New Rectangle With {
                .Height = 2
            }
            ' Peak caps read as a bright highlight against the panel in every theme - Dark bows out
            ' since its peaks are near-white already; TextPrimaryBrush keeps the same contrast logic
            ' the rest of the panel's text uses instead of a hardcoded White that would vanish on Light.
            peak.SetResourceReference(Shape.FillProperty, "TextPrimaryBrush")
            BarsCanvas.Children.Add(reflection)
            BarsCanvas.Children.Add(bar)
            BarsCanvas.Children.Add(peak)
            reflections(i) = reflection
            bars(i) = bar
            peaks(i) = peak
        Next
    End Sub

    ''' <summary>Faint horizontal meter lines behind the bars, redrawn whenever the window resizes.</summary>
    Private Sub BuildGridLines()
        GridLinesCanvas.Children.Clear()
        Dim w = EqRoot.ActualWidth
        Dim h = MainAreaHeight()
        If w <= 0 OrElse h <= 0 Then Return

        Const lineCount As Integer = 5
        For i = 1 To lineCount
            Dim y = h * i / (lineCount + 1)
            Dim line As New Rectangle With {
                .Width = w, .Height = 1,
                .Fill = gridLineBrush
            }
            Canvas.SetLeft(line, 0)
            Canvas.SetTop(line, y)
            GridLinesCanvas.Children.Add(line)
        Next
    End Sub

    Private Function MainAreaHeight() As Double
        Return Math.Max(0, EqRoot.ActualHeight * (1 - ReflectionHeightRatio) - ReflectionGap)
    End Function

    Private Sub RenderTimer_Tick(sender As Object, e As EventArgs)
        Dim dt = renderTimer.Interval.TotalSeconds
        elapsedSeconds += dt
        energy += (targetEnergy - energy) * Math.Min(1, dt * 3)
        If Math.Abs(energy - targetEnergy) < 0.001 Then energy = targetEnergy

        Dim w = EqRoot.ActualWidth
        Dim mainHeight = MainAreaHeight()
        Dim reflectionHeight = EqRoot.ActualHeight * ReflectionHeightRatio
        If w <= 0 OrElse mainHeight <= 0 Then Return

        Dim slotWidth = w / BarCount
        Dim barWidth = Math.Max(1, slotWidth * (1 - BarGapRatio))

        For i = 0 To BarCount - 1
            Dim wave = 0.55 * (0.5 + 0.5 * Math.Sin(elapsedSeconds * freqA(i) + phaseA(i))) +
                       0.45 * (0.5 + 0.5 * Math.Sin(elapsedSeconds * freqB(i) + phaseB(i)))
            Dim jitter = (rng.NextDouble() - 0.5) * 0.08
            Dim level = Math.Max(0, Math.Min(1, wave + jitter))

            Dim target = level * mainHeight * energy
            currentHeight(i) += (target - currentHeight(i)) * 0.4

            Dim x = i * slotWidth + (slotWidth - barWidth) / 2

            ' Main bar, anchored to the bottom of the main area.
            Dim bar = bars(i)
            bar.Width = barWidth
            bar.Height = currentHeight(i)
            Canvas.SetLeft(bar, x)
            Canvas.SetTop(bar, mainHeight - currentHeight(i))

            ' Peak cap: jumps up instantly with the bar, then falls on its own under "gravity".
            If currentHeight(i) >= peakHeight(i) Then
                peakHeight(i) = currentHeight(i)
                peakVelocity(i) = 0
            Else
                peakVelocity(i) += 260 * dt
                peakHeight(i) = Math.Max(currentHeight(i), peakHeight(i) - peakVelocity(i) * dt)
            End If
            Dim peak = peaks(i)
            peak.Width = barWidth
            peak.Opacity = If(energy > 0.02, 0.9, 0)
            Canvas.SetLeft(peak, x)
            Canvas.SetTop(peak, mainHeight - peakHeight(i) - peak.Height)

            ' Reflection: mirrored, shorter, and faded - purely decorative.
            Dim reflection = reflections(i)
            Dim reflectionLevel = currentHeight(i) / mainHeight
            Dim rHeight = reflectionLevel * reflectionHeight
            reflection.Width = barWidth
            reflection.Height = rHeight
            reflection.Opacity = 0.28 * Math.Max(0.15, reflectionLevel)
            Canvas.SetLeft(reflection, x)
            Canvas.SetTop(reflection, mainHeight + ReflectionGap)
        Next
    End Sub

End Class

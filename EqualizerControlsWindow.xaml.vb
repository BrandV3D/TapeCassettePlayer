Imports NAudio.Effects

''' <summary>Real graphic-equalizer controls: 10 band sliders, a Preamp knob, a one-turn Bass Boost
''' knob, genre presets, and an EQ On/Off toggle - all operating on the <see cref="AudioEngine"/>'s
''' persistent <see cref="GraphicEqualizer"/>, so unlike EqualizerWindow (the decorative bar-graph
''' visual) or VisualizerWindow (the decorative waveform), moving these controls actually reshapes
''' the sound coming out of the speakers.</summary>
Class EqualizerControlsWindow

    Private ReadOnly engine As AudioEngine
    Private ReadOnly bandSliders As Slider()
    Private ReadOnly BassBands As Integer() = {0, 1, 2} ' lowest three TenBandOctave bands

    ' External setters returned by SetupKnob, so Reset can snap a knob back to its default without
    ' re-attaching a whole new set of mouse handlers on top of the existing ones.
    Private setPreampKnob As Action(Of Double)
    Private setBassBoostKnob As Action(Of Double)

    Private ReadOnly Presets As New List(Of (Name As String, Gains As Double())) From {
        ("Flat", New Double() {0, 0, 0, 0, 0, 0, 0, 0, 0, 0}),
        ("Rock", New Double() {5, 3, -2, -4, -2, 1, 4, 6, 6, 6}),
        ("Pop", New Double() {-2, -1, 0, 2, 4, 4, 2, 0, -1, -2}),
        ("Jazz", New Double() {3, 2, 1, 1, -1, -1, 0, 1, 2, 3}),
        ("Classical", New Double() {4, 3, 2, 0, 0, 0, -1, -1, -1, 3}),
        ("Bass Boost", New Double() {8, 7, 5, 3, 1, 0, 0, 0, 0, 0}),
        ("Treble Boost", New Double() {0, 0, 0, 0, 0, 1, 3, 5, 7, 8}),
        ("Vocal", New Double() {-3, -3, -2, 1, 4, 4, 3, 1, -1, -2})
    }

    Public Sub New(engine As AudioEngine)
        InitializeComponent()
        Me.engine = engine
        engine.Equalizer.Bypass = False ' matches BypassToggle's initial IsChecked="True" ("EQ ON")
        bandSliders = New Slider(engine.Equalizer.BandCount - 1) {}

        BuildBandSliders()
        BuildPresetButtons()

        AddHandler BypassToggle.Checked, Sub() engine.Equalizer.Bypass = False
        AddHandler BypassToggle.Unchecked, Sub() engine.Equalizer.Bypass = True
        AddHandler ResetButton.Click, AddressOf ResetButton_Click

        setPreampKnob = SetupKnob(PreampKnobEllipse, PreampKnobRotate, 0, 2, 1, Sub(v) engine.Preamp = CSng(v))
        setBassBoostKnob = SetupKnob(BassBoostKnobEllipse, BassBoostKnobRotate, 0, 12, 0, AddressOf ApplyBassBoost)
    End Sub

    ''' <summary>Builds one vertical slider per band straight from the engine's GraphicEqualizer
    ''' (frequency, current gain), so this always matches whatever layout the engine was built
    ''' with instead of assuming exactly 10 bands.</summary>
    Private Sub BuildBandSliders()
        For i = 0 To engine.Equalizer.BandCount - 1
            Dim freq = engine.Equalizer.GetCentreFrequency(i)

            Dim valueLabel As New TextBlock With {
                .Text = "0", .Foreground = Brushes.White, .FontSize = 10,
                .HorizontalAlignment = HorizontalAlignment.Center
            }
            Dim slider As New Slider With {
                .Orientation = Orientation.Vertical,
                .Minimum = -12, .Maximum = 12, .Value = 0,
                .Height = 150, .Width = 24,
                .TickFrequency = 3, .IsSnapToTickEnabled = False,
                .Margin = New Thickness(6, 4, 6, 4)
            }
            Dim freqLabel As New TextBlock With {
                .Text = FormatFrequency(freq), .Foreground = Brushes.White, .FontSize = 10,
                .HorizontalAlignment = HorizontalAlignment.Center
            }

            Dim index = i
            AddHandler slider.ValueChanged, Sub(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
                                                 engine.Equalizer.SetBandGain(index, CSng(e.NewValue))
                                                 valueLabel.Text = $"{e.NewValue:+0;-0;0}"
                                             End Sub

            Dim column As New StackPanel With {.HorizontalAlignment = HorizontalAlignment.Center}
            column.Children.Add(valueLabel)
            column.Children.Add(slider)
            column.Children.Add(freqLabel)
            BandsPanel.Children.Add(column)

            bandSliders(i) = slider
        Next
    End Sub

    Private Shared Function FormatFrequency(freqHz As Single) As String
        If freqHz >= 1000 Then Return $"{freqHz / 1000:0.#}k"
        Return $"{freqHz:0}"
    End Function

    Private Sub BuildPresetButtons()
        For Each preset In Presets
            Dim gains = preset.Gains
            Dim btn As New Button With {
                .Content = preset.Name,
                .Style = TryCast(FindResource("EqButton"), Style)
            }
            AddHandler btn.Click, Sub() ApplyPreset(gains)
            PresetsPanel.Children.Add(btn)
        Next
    End Sub

    ''' <summary>Presets only touch the band sliders (each slider's own ValueChanged handler is what
    ''' actually calls SetBandGain), so there's exactly one place that writes to the equalizer.</summary>
    Private Sub ApplyPreset(gains As Double())
        For i = 0 To Math.Min(bandSliders.Length, gains.Length) - 1
            bandSliders(i).Value = gains(i)
        Next
    End Sub

    ''' <summary>The Bass Boost knob is a convenience macro, not a separate audio parameter - turning
    ''' it just drives the bottom three band sliders to the same dB value; the sliders' own
    ''' ValueChanged handlers do the actual work. Nudging one of those sliders by hand afterwards
    ''' simply overrides the knob's contribution for that band, same as turning a real mixer knob
    ''' after a fader.</summary>
    Private Sub ApplyBassBoost(value As Double)
        For Each i In BassBands
            If i < bandSliders.Length Then bandSliders(i).Value = value
        Next
    End Sub

    Private Sub ResetButton_Click(sender As Object, e As RoutedEventArgs)
        ApplyPreset(Presets(0).Gains) ' "Flat"
        setPreampKnob(1)
        setBassBoostKnob(0)
    End Sub

    ''' <summary>Wires an Ellipse up as a rotary knob: vertical drag changes the value (dragging up
    ''' increases it), the indicator rotates to match across a 270-degree sweep, and
    ''' <paramref name="onChange"/> fires with every change. Returns a setter that moves the knob to
    ''' an exact value (and fires onChange) programmatically, without touching the mouse handlers -
    ''' used by Reset so it doesn't end up stacking a second set of handlers on the same knob.</summary>
    Private Function SetupKnob(ellipse As Ellipse, indicator As RotateTransform, minVal As Double, maxVal As Double, initial As Double, onChange As Action(Of Double)) As Action(Of Double)
        Dim value = initial
        Dim dragging = False
        Dim dragStartY As Double = 0
        Dim dragStartValue As Double = 0

        Dim applyRotation =
            Sub()
                Dim t = (value - minVal) / (maxVal - minVal)
                indicator.Angle = -135 + t * 270
            End Sub
        applyRotation()

        AddHandler ellipse.MouseLeftButtonDown,
            Sub(sender As Object, e As MouseButtonEventArgs)
                dragging = True
                dragStartY = e.GetPosition(Me).Y
                dragStartValue = value
                ellipse.CaptureMouse()
            End Sub

        AddHandler ellipse.MouseMove,
            Sub(sender As Object, e As MouseEventArgs)
                If Not dragging Then Return
                Dim deltaY = dragStartY - e.GetPosition(Me).Y
                Dim range = maxVal - minVal
                Dim newValue = dragStartValue + deltaY / 150.0 * range
                value = Math.Max(minVal, Math.Min(maxVal, newValue))
                applyRotation()
                onChange(value)
            End Sub

        AddHandler ellipse.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                dragging = False
                ellipse.ReleaseMouseCapture()
            End Sub

        Return Sub(v As Double)
                   value = Math.Max(minVal, Math.Min(maxVal, v))
                   applyRotation()
                   onChange(value)
               End Sub
    End Function

End Class

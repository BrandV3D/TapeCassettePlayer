Imports System.IO
Imports System.Text
Imports System.Windows.Media.Imaging

''' <summary>Extracts embedded cover art from an audio file: the ID3v2 APIC frame for MP3, or the
''' PICTURE metadata block for FLAC. WAV isn't checked - cover art isn't a standard WAV feature and
''' none of this app's own WAV mixtape output ever embeds any (see MixtapeBuilder).</summary>
Public Class AlbumArtReader

    ''' <summary>Returns the embedded cover art for <paramref name="path"/> as a frozen
    ''' BitmapImage, or Nothing if the file has none (or isn't a format this understands).</summary>
    Public Shared Function TryGetAlbumArt(path As String) As BitmapImage
        Dim bytes = TryGetAlbumArtBytes(path)
        If bytes Is Nothing Then Return Nothing

        Try
            Dim image As New BitmapImage()
            image.BeginInit()
            image.CacheOption = BitmapCacheOption.OnLoad
            image.StreamSource = New MemoryStream(bytes)
            image.EndInit()
            image.Freeze()
            Return image
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Function TryGetAlbumArtBytes(filePath As String) As Byte()
        Try
            Select Case Path.GetExtension(filePath).ToLowerInvariant()
                Case ".mp3"
                    Return ReadId3Apic(filePath)
                Case ".flac"
                    Return ReadFlacPicture(filePath)
                Case Else
                    Return Nothing
            End Select
        Catch
            Return Nothing
        End Try
    End Function

    ' ---- MP3: ID3v2 APIC frame -------------------------------------------

    ''' <summary>Walks an ID3v2 tag's frames the same way MixtapeBuilder.ReadLabel does, looking
    ''' for APIC (embedded picture) instead of TIT2/TXXX.</summary>
    Private Shared Function ReadId3Apic(path As String) As Byte()
        Using stream = File.OpenRead(path)
            Dim header(9) As Byte
            If stream.Read(header, 0, 10) <> 10 Then Return Nothing
            If Encoding.ASCII.GetString(header, 0, 3) <> "ID3" Then Return Nothing

            Dim majorVersion = header(3)
            Dim tagSize = ReadSyncSafeInt(header, 6)
            If tagSize <= 0 Then Return Nothing

            Dim tagBytes(tagSize - 1) As Byte
            If stream.Read(tagBytes, 0, tagSize) <> tagSize Then Return Nothing

            Dim pos = 0
            While pos + 10 <= tagBytes.Length
                Dim frameId = Encoding.ASCII.GetString(tagBytes, pos, 4)
                If frameId = Chr(0) & Chr(0) & Chr(0) & Chr(0) Then Exit While

                Dim frameSize = If(majorVersion >= 4,
                    ReadSyncSafeInt(tagBytes, pos + 4),
                    ReadBigEndianUInt32(tagBytes, pos + 4))
                If frameSize <= 0 Then Exit While

                Dim contentStart = pos + 10
                If contentStart + frameSize > tagBytes.Length Then Exit While

                If frameId = "APIC" Then Return DecodeApic(tagBytes, contentStart, frameSize)

                pos = contentStart + frameSize
            End While

            Return Nothing
        End Using
    End Function

    ''' <summary>Decodes an APIC frame's content: an encoding byte, a null-terminated MIME type, a
    ''' picture-type byte, a null-terminated description (terminator width depends on the text
    ''' encoding), then the raw picture bytes filling out the rest of the frame.</summary>
    Private Shared Function DecodeApic(bytes As Byte(), offset As Integer, length As Integer) As Byte()
        Dim frameEnd = offset + length
        Dim pos = offset
        If pos >= frameEnd Then Return Nothing

        Dim encodingByte = bytes(pos)
        pos += 1

        Dim mimeEnd = Array.IndexOf(bytes, CByte(0), pos, frameEnd - pos)
        If mimeEnd < 0 Then Return Nothing
        pos = mimeEnd + 1

        If pos >= frameEnd Then Return Nothing
        pos += 1 ' picture-type byte

        Dim descTerminatorWidth = If(encodingByte = 1 OrElse encodingByte = 2, 2, 1)
        Dim descEnd = FindNullTerminator(bytes, pos, frameEnd, descTerminatorWidth)
        If descEnd < 0 Then Return Nothing
        pos = descEnd + descTerminatorWidth

        If pos > frameEnd Then Return Nothing
        Dim pictureLength = frameEnd - pos
        If pictureLength <= 0 Then Return Nothing

        Dim picture(pictureLength - 1) As Byte
        Array.Copy(bytes, pos, picture, 0, pictureLength)
        Return picture
    End Function

    ''' <summary>Finds a null terminator - 1 byte for Latin-1/UTF-8, an aligned 0x0000 pair for
    ''' UTF-16 - at or after <paramref name="start"/> and before <paramref name="end"/>.</summary>
    Private Shared Function FindNullTerminator(bytes As Byte(), start As Integer, [end] As Integer, terminatorWidth As Integer) As Integer
        Dim i = start
        While i + terminatorWidth <= [end]
            If bytes(i) = 0 AndAlso (terminatorWidth = 1 OrElse bytes(i + 1) = 0) Then Return i
            i += terminatorWidth
        End While
        Return -1
    End Function

    ''' <summary>Widens each byte to Integer before shifting - VB's shift operators mask the shift
    ''' count modulo the OPERAND's declared bit width (8 for Byte), not the promoted result type's,
    ''' so shifting a bare Byte by 8 or more silently becomes a no-op shift instead of overflowing
    ''' into a wider type the way it would in most other languages.</summary>
    Private Shared Function ReadSyncSafeInt(bytes As Byte(), offset As Integer) As Integer
        Return (CInt(bytes(offset)) << 21) Or (CInt(bytes(offset + 1)) << 14) Or (CInt(bytes(offset + 2)) << 7) Or bytes(offset + 3)
    End Function

    ' ---- FLAC: PICTURE metadata block -------------------------------------

    Private Shared Function ReadFlacPicture(path As String) As Byte()
        Using stream = File.OpenRead(path)
            Dim magic(3) As Byte
            If stream.Read(magic, 0, 4) <> 4 Then Return Nothing
            If Encoding.ASCII.GetString(magic, 0, 4) <> "fLaC" Then Return Nothing

            Dim blockHeader(3) As Byte
            Do
                If stream.Read(blockHeader, 0, 4) <> 4 Then Return Nothing
                Dim isLast = (blockHeader(0) And &H80) <> 0
                Dim blockType = blockHeader(0) And &H7F
                Dim blockLength = (CInt(blockHeader(1)) << 16) Or (CInt(blockHeader(2)) << 8) Or blockHeader(3)

                If blockType = 6 Then ' PICTURE
                    If blockLength <= 0 Then Return Nothing
                    Dim blockData(blockLength - 1) As Byte
                    If stream.Read(blockData, 0, blockLength) <> blockLength Then Return Nothing
                    Return DecodeFlacPicture(blockData)
                End If

                stream.Seek(blockLength, SeekOrigin.Current)
                If isLast Then Return Nothing
            Loop
        End Using
    End Function

    ''' <summary>Per the FLAC PICTURE block spec: picture type (4 bytes), MIME type string
    ''' (length-prefixed), description string (length-prefixed), width/height/depth/colors-used
    ''' (4 bytes each, ignored here), then the length-prefixed picture data itself.</summary>
    Private Shared Function DecodeFlacPicture(data As Byte()) As Byte()
        Dim pos = 4 ' picture type

        Dim mimeLength = ReadBigEndianUInt32(data, pos)
        pos += 4 + mimeLength

        If pos + 4 > data.Length Then Return Nothing
        Dim descLength = ReadBigEndianUInt32(data, pos)
        pos += 4 + descLength

        pos += 16 ' width, height, color depth, colors used

        If pos + 4 > data.Length Then Return Nothing
        Dim pictureLength = ReadBigEndianUInt32(data, pos)
        pos += 4

        If pictureLength <= 0 OrElse pos + pictureLength > data.Length Then Return Nothing

        Dim picture(pictureLength - 1) As Byte
        Array.Copy(data, pos, picture, 0, pictureLength)
        Return picture
    End Function

    Private Shared Function ReadBigEndianUInt32(bytes As Byte(), offset As Integer) As Integer
        Return (CInt(bytes(offset)) << 24) Or (CInt(bytes(offset + 1)) << 16) Or (CInt(bytes(offset + 2)) << 8) Or bytes(offset + 3)
    End Function

End Class

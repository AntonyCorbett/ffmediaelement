namespace Unosquare.FFME.Tests;

using ClosedCaptions;
using System;
using Xunit;

/// <summary>
/// Tests for <see cref="ClosedCaptionPacket"/> EIA-608 parsing.
/// All tests are pure C# — no FFmpeg binaries required.
///
/// Header byte conventions used in helpers:
///   0xFC = 11111100 → markers ✓, valid flag ✓, field 1 (bits 00)
///   0xFD = 11111101 → markers ✓, valid flag ✓, field 2 (bits 01)
///   0xF8 = 11111000 → markers ✓, valid flag ✗ → NullPad
///   0x04 = 00000100 → markers ✗               → NullPad
/// </summary>
public sealed class ClosedCaptionsTests
{
    private static readonly TimeSpan T = TimeSpan.FromSeconds(1);

    // -------------------------------------------------------------------------
    // NullPad
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_NullBytes_IsNullPad()
    {
        var p = Packet(0xFC, 0x00, 0x00);
        Assert.Equal(CaptionsPacketType.NullPad, p.PacketType);
    }

    [Fact]
    public void Packet_NoHeaderMarkers_IsNullPad()
    {
        // 0x04 has no top-5-bit marker → NullPad
        var p = Packet(0x04, 0x20, 0x41);
        Assert.Equal(CaptionsPacketType.NullPad, p.PacketType);
    }

    [Fact]
    public void Packet_ValidFlagNotSet_IsNullPad()
    {
        // 0xF8 has markers but bit 2 (0x04) is 0
        var p = Packet(0xF8, 0x20, 0x41);
        Assert.Equal(CaptionsPacketType.NullPad, p.PacketType);
    }

    // -------------------------------------------------------------------------
    // Text
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_AsciiText_ParsesCorrectly()
    {
        // D0=0x48 ('H'), D1=0x69 ('i') → basic North American text
        var p = Packet(0xFC, 0x48, 0x69);
        Assert.Equal(CaptionsPacketType.Text, p.PacketType);
        Assert.Equal("Hi", p.Text);
    }

    [Fact]
    public void Packet_SingleAsciiChar_D1Zero_IsOneChar()
    {
        // D1=0x00 means only one character
        var p = Packet(0xFC, 0x41, 0x00);
        Assert.Equal(CaptionsPacketType.Text, p.PacketType);
        Assert.Equal("A", p.Text);
    }

    [Fact]
    public void Packet_Eia608SpecialChar_AlteredMapping()
    {
        // 0x2A maps to 'á' per Annex A Table 68
        var p = Packet(0xFC, 0x2A, 0x00);
        Assert.Equal("á", p.Text);
    }

    [Fact]
    public void Packet_Eia608Block_0x7F_MapsToBoldBlock()
    {
        var p = Packet(0xFC, 0x7F, 0x00);
        Assert.Equal("█", p.Text);
    }

    [Fact]
    public void Packet_SpecialNorthAmerican_Copyright_Parsed()
    {
        // D0=0x11, D1=0x30 → '®'
        var p = Packet(0xFC, 0x11, 0x30);
        Assert.Equal(CaptionsPacketType.Text, p.PacketType);
        Assert.Equal("®", p.Text);
    }

    [Fact]
    public void Packet_SpecialNorthAmerican_Degree_Parsed()
    {
        // D0=0x11, D1=0x31 → '°'
        var p = Packet(0xFC, 0x11, 0x31);
        Assert.Equal("°", p.Text);
    }

    // -------------------------------------------------------------------------
    // MidRow
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_MidRow_Channel1_ParsedCorrectly()
    {
        // D0=0x11, D1=0x20 → MidRow, channel 1
        var p = Packet(0xFC, 0x11, 0x20);
        Assert.Equal(CaptionsPacketType.MidRow, p.PacketType);
        Assert.Equal(1, p.FieldChannel);
    }

    [Fact]
    public void Packet_MidRow_Channel2_ParsedCorrectly()
    {
        // D0=0x19, D1=0x20 → MidRow, channel 2
        var p = Packet(0xFC, 0x19, 0x20);
        Assert.Equal(CaptionsPacketType.MidRow, p.PacketType);
        Assert.Equal(2, p.FieldChannel);
    }

    // -------------------------------------------------------------------------
    // Command
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_MiscCommand_Parsed()
    {
        // D0=0x14 (channel 1), D1=0x20
        var p = Packet(0xFC, 0x14, 0x20);
        Assert.Equal(CaptionsPacketType.Command, p.PacketType);
    }

    [Fact]
    public void Packet_TabCommand_ParsesTabCount()
    {
        // D0=0x17, D1=0x21 → 1 tab (D1 & 0x03 = 1)
        var p = Packet(0xFC, 0x17, 0x21);
        Assert.Equal(CaptionsPacketType.Tabs, p.PacketType);
        Assert.Equal(1, p.Tabs);
    }

    [Fact]
    public void Packet_TabCommand_ThreeTabs()
    {
        // D0=0x17, D1=0x23 → 3 tabs
        var p = Packet(0xFC, 0x17, 0x23);
        Assert.Equal(3, p.Tabs);
    }

    // -------------------------------------------------------------------------
    // Preamble
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_Preamble_Row11_Parsed()
    {
        // D0=0x10 → row 11, D1=0x40 in 0x40-0x5F range
        var p = Packet(0xFC, 0x10, 0x40);
        Assert.Equal(CaptionsPacketType.Preamble, p.PacketType);
        Assert.Equal(11, p.PreambleRow);
    }

    [Fact]
    public void Packet_Preamble_Channel1_Parsed()
    {
        // D0=0x11 (channel 1), D1=0x40
        var p = Packet(0xFC, 0x11, 0x40);
        Assert.Equal(CaptionsPacketType.Preamble, p.PacketType);
        Assert.Equal(1, p.FieldChannel);
    }

    [Fact]
    public void Packet_Preamble_Channel2_Parsed()
    {
        // D0=0x19 (channel 2), D1=0x40
        var p = Packet(0xFC, 0x19, 0x40);
        Assert.Equal(CaptionsPacketType.Preamble, p.PacketType);
        Assert.Equal(2, p.FieldChannel);
    }

    // -------------------------------------------------------------------------
    // XDS
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_XdsClass_Parsed()
    {
        // D0=0x01 → XDS (only lower nibble, non-zero)
        var p = Packet(0xFC, 0x01, 0x00);
        Assert.Equal(CaptionsPacketType.XdsClass, p.PacketType);
        Assert.Equal(CaptionsXdsClass.CurrentStart, p.XdsClass);
    }

    [Fact]
    public void Packet_XdsClass_IsNotControlPacket()
    {
        var p = Packet(0xFC, 0x01, 0x00);
        Assert.False(p.IsControlPacket);
    }

    // -------------------------------------------------------------------------
    // Color
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_Color_Channel1_Parsed()
    {
        // D0=0x10 (channel 1), D1=0x20 → Color
        var p = Packet(0xFC, 0x10, 0x20);
        Assert.Equal(CaptionsPacketType.Color, p.PacketType);
        Assert.Equal(1, p.FieldChannel);
    }

    // -------------------------------------------------------------------------
    // IsControlPacket
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_D0InControlRange_IsControlPacket()
    {
        // D0 in 0x10-0x1F
        var p = Packet(0xFC, 0x14, 0x20);
        Assert.True(p.IsControlPacket);
    }

    [Fact]
    public void Packet_D0OutsideControlRange_IsNotControlPacket()
    {
        var p = Packet(0xFC, 0x41, 0x42); // ASCII text
        Assert.False(p.IsControlPacket);
    }

    // -------------------------------------------------------------------------
    // IsRepeatedControlCode
    // -------------------------------------------------------------------------

    [Fact]
    public void IsRepeatedControlCode_SameControlPacket_ReturnsTrue()
    {
        var prev = Packet(0xFC, 0x14, 0x20);
        var curr = Packet(0xFC, 0x14, 0x20);
        Assert.True(curr.IsRepeatedControlCode(prev));
    }

    [Fact]
    public void IsRepeatedControlCode_DifferentD1_ReturnsFalse()
    {
        var prev = Packet(0xFC, 0x14, 0x20);
        var curr = Packet(0xFC, 0x14, 0x21);
        Assert.False(curr.IsRepeatedControlCode(prev));
    }

    [Fact]
    public void IsRepeatedControlCode_NullPrevious_ReturnsFalse()
    {
        var curr = Packet(0xFC, 0x14, 0x20);
        Assert.False(curr.IsRepeatedControlCode(null));
    }

    [Fact]
    public void IsRepeatedControlCode_NotControlPacket_ReturnsFalse()
    {
        var prev = Packet(0xFC, 0x41, 0x42); // text, not control
        var curr = Packet(0xFC, 0x41, 0x42);
        Assert.False(curr.IsRepeatedControlCode(prev));
    }

    // -------------------------------------------------------------------------
    // ComputeChannel
    // -------------------------------------------------------------------------

    [Fact]
    public void ComputeChannel_Parity1Channel1_IsCC1() =>
        Assert.Equal(CaptionsChannel.CC1, ClosedCaptionPacket.ComputeChannel(1, 1));

    [Fact]
    public void ComputeChannel_Parity1Channel2_IsCC2() =>
        Assert.Equal(CaptionsChannel.CC2, ClosedCaptionPacket.ComputeChannel(1, 2));

    [Fact]
    public void ComputeChannel_Parity2Channel1_IsCC3() =>
        Assert.Equal(CaptionsChannel.CC3, ClosedCaptionPacket.ComputeChannel(2, 1));

    [Fact]
    public void ComputeChannel_Parity2Channel2_IsCC4() =>
        Assert.Equal(CaptionsChannel.CC4, ClosedCaptionPacket.ComputeChannel(2, 2));

    // -------------------------------------------------------------------------
    // Timestamp & sorting
    // -------------------------------------------------------------------------

    [Fact]
    public void Packet_Timestamp_IsPreserved()
    {
        var ts = TimeSpan.FromSeconds(3.5);
        var p = new ClosedCaptionPacket(ts, 0xFC, 0x41, 0x42);
        Assert.Equal(ts, p.Timestamp);
    }

    [Fact]
    public void Packet_CompareTo_SortsByTimestamp()
    {
        var early = new ClosedCaptionPacket(TimeSpan.FromSeconds(1), 0xFC, 0x41, 0x00);
        var late = new ClosedCaptionPacket(TimeSpan.FromSeconds(2), 0xFC, 0x42, 0x00);
        Assert.True(early.CompareTo(late) < 0);
        Assert.True(late.CompareTo(early) > 0);
    }

    [Fact]
    public void Packet_ParityBitDropped_D0IsLower7Bits()
    {
        // D0 = 0xC8 → parity bit stripped → 0x48 = 'H'
        var p = Packet(0xFC, 0xC8, 0x00); // 0xC8 & 0x7F = 0x48
        Assert.Equal(0x48, p.D0);
    }

    [Fact]
    public void Packet_FieldParity_Field1Header_IsOne()
    {
        var p = Packet(0xFC, 0x41, 0x00); // 0xFC & 0x03 = 0x00 → field 1
        Assert.Equal(1, p.FieldParity);
    }

    [Fact]
    public void Packet_FieldParity_Field2Header_IsTwo()
    {
        var p = Packet(0xFD, 0x41, 0x00); // 0xFD & 0x03 = 0x01 → field 2
        Assert.Equal(2, p.FieldParity);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

#pragma warning disable U2U1012
    private static ClosedCaptionPacket Packet(byte header, byte d0, byte d1) =>
        new(T, header, d0, d1);
#pragma warning restore U2U1012
}

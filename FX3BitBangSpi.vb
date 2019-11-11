﻿'File:          FX3BitBangSpi.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          08/27/2019
'Description:   This file contains all the interfacing functions for the FX3 SPI bitbang capabilities.

Imports FX3USB

Partial Class FX3Connection

    ''' <summary>
    ''' Property to get or set the bit bang SPI configuration. Can select pins, timings, etc
    ''' </summary>
    ''' <returns></returns>
    Public Property BitBangSpiConfig As BitBangSpiConfig
        Get
            Return m_BitBangSpi
        End Get
        Set(value As BitBangSpiConfig)
            m_BitBangSpi = value
        End Set
    End Property

    ''' <summary>
    ''' Perform a bit banged SPI transfer, using the config set in BitBangSpiConfig.
    ''' </summary>
    ''' <param name="BitsPerTransfer">The total number of bits to clock in a single transfer. Can be any number greater than 0.</param>
    ''' <param name="NumTransfers">The number of separate SPI transfers to clock out</param>
    ''' <param name="MOSIData">The MOSI data to clock out. Each SPI transfer must be byte aligned. Data is clocked out MSB first</param>
    ''' <param name="TimeoutInMs">The time to wait on the bulk endpoint for a return transfer (in ms)</param>
    ''' <returns>The data received over the selected MISO line</returns>
    Public Function BitBangSpi(BitsPerTransfer As UInteger, NumTransfers As UInteger, MOSIData As Byte(), TimeoutInMs As UInteger) As Byte()
        Dim buf As New List(Of Byte)
        Dim timeoutTimer As New Stopwatch()
        Dim transferStatus As Boolean
        Dim bytesPerTransfer As UInteger
        Dim len As Integer

        'Validate bits per transfer
        If BitsPerTransfer = 0 Then
            Throw New FX3ConfigurationException("ERROR: Bits per transfer must be non-zero in a bit banged SPI transfer")
        End If

        'Return for 0 transfers
        If NumTransfers = 0 Then
            Return buf.ToArray()
        End If

        'Build the buffer
        buf.AddRange(m_BitBangSpi.GetParameterArray())
        buf.Add(BitsPerTransfer And &HFF)
        buf.Add((BitsPerTransfer And &HFF00) >> 8)
        buf.Add((BitsPerTransfer And &HFF0000) >> 16)
        buf.Add((BitsPerTransfer And &HFF000000) >> 24)
        buf.Add(NumTransfers And &HFF)
        buf.Add((NumTransfers And &HFF00) >> 8)
        buf.Add((NumTransfers And &HFF0000) >> 16)
        buf.Add((NumTransfers And &HFF000000) >> 24)
        buf.AddRange(MOSIData)

        'Find bytes per transfer
        bytesPerTransfer = BitsPerTransfer >> 3
        If BitsPerTransfer And &H7 Then bytesPerTransfer += 1

        If bytesPerTransfer * NumTransfers <> MOSIData.Count() Then
            Throw New FX3ConfigurationException("ERROR: MOSI data size must match total transfer size")
        End If

        'Check size
        If buf.Count() > 4096 Then
            Throw New FX3ConfigurationException("ERROR: Too much data (" + buf.Count() + " bytes) in a single bit banged SPI transaction.")
        End If

        'Send the start command
        ConfigureControlEndpoint(USBCommands.ADI_BITBANG_SPI, True)
        If Not XferControlData(buf.ToArray(), buf.Count(), 2000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer failed for SPI bit bang setup")
        End If

        'Read data back from part
        transferStatus = False
        timeoutTimer.Start()
        Dim resultBuf(bytesPerTransfer * NumTransfers - 1) As Byte
        While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < TimeoutInMs))
            len = bytesPerTransfer * NumTransfers
            transferStatus = USB.XferData(resultBuf, len, DataInEndPt)
        End While
        timeoutTimer.Stop()

        If Not transferStatus Then
            Console.WriteLine("ERROR: Bit bang SPI transfer timed out")
        End If

        Return resultBuf
    End Function

    ''' <summary>
    ''' Read a standard iSensors 16-bit register using a bitbang SPI connection
    ''' </summary>
    ''' <param name="addr">The address of the register to read</param>
    ''' <returns>The register value</returns>
    Public Function BitBangReadReg16(addr As UInteger) As UShort
        Dim MOSI As New List(Of Byte)
        Dim buf() As Byte
        Dim retValue, shift As UShort
        addr = addr And &HFF
        MOSI.Add(addr)
        MOSI.Add(0)
        MOSI.Add(0)
        MOSI.Add(0)
        buf = BitBangSpi(16, 2, MOSI.ToArray(), m_StreamTimeout)
        shift = buf(2)
        shift = shift << 8
        retValue = shift + buf(3)
        Return retValue
    End Function

    ''' <summary>
    ''' Write a byte to an iSensor register using a bitbang SPI connection
    ''' </summary>
    ''' <param name="addr">The address of the register to write to</param>
    ''' <param name="data">The data to write to the register</param>
    Public Sub BitBangWriteRegByte(addr As Byte, data As Byte)
        Dim MOSI As New List(Of Byte)
        'Addr first (with write bit)
        addr = addr And &HFF
        addr = addr Or &H80
        MOSI.Add(addr)
        MOSI.Add(data)
        'Call bitbang SPI implementation
        BitBangSpi(16, 1, MOSI.ToArray(), m_StreamTimeout)
    End Sub

    ''' <summary>
    ''' Resets the hardware SPI pins to their default operating mode. Can be used to recover the SPI functionality after a bit-bang SPI transaction over the hardware SPI pins
    ''' without having to reboot the FX3.
    ''' </summary>
    Public Sub RestoreHardwareSpi()
        Dim buf(3) As Byte
        Dim status As UInt32
        ConfigureControlEndpoint(USBCommands.ADI_RESET_SPI, False)
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer failed for SPI hardware controller reset")
        End If
        status = BitConverter.ToUInt32(buf, 0)
        If status <> 0 Then
            Throw New FX3BadStatusException("ERROR: Invalid status received after SPI hardware controller reset: 0x" + status.ToString("X4"))
        End If
    End Sub

    ''' <summary>
    ''' Set the SCLK frequency for a bit banged SPI connection. Overloaded to allow for a UInt
    ''' </summary>
    ''' <param name="Freq">The SPI frequency, in Hz</param>
    ''' <returns>A boolean indicating if the frequency could be set.</returns>
    Public Function SetBitBangSpiFreq(Freq As UInteger) As Boolean

        Dim Freq_Dbl As Double = Convert.ToDouble(Freq)
        Return SetBitBangSpiFreq(Freq_Dbl)

    End Function

    ''' <summary>
    ''' Sets the SCLK frequency for a bit bang SPI connection. 
    ''' </summary>
    ''' <param name="Freq">The desired SPI frequency. Can go from 800KHz to approx 0.001Hz</param>
    ''' <returns></returns>
    Public Function SetBitBangSpiFreq(Freq As Double) As Boolean
        Dim halfPeriodNsOffset As Double = 740
        'Check if freq is more than max capable freq
        If Freq > (1 / (2 * halfPeriodNsOffset * 10 ^ -9)) Then
            m_BitBangSpi.SCLKHalfPeriodTicks = 0
            Return False
        End If

        Dim desiredPeriodNS As Double
        desiredPeriodNS = 10 ^ 9 / Freq
        'There is a base half clock period of 740ns (atm). Each value added to that adds an additional 62ns
        desiredPeriodNS = desiredPeriodNS / 2
        desiredPeriodNS = desiredPeriodNS - halfPeriodNsOffset
        m_BitBangSpi.SCLKHalfPeriodTicks = Math.Floor(desiredPeriodNS / 62)

        Return True
    End Function

End Class

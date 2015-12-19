﻿using System;
using System.IO.Ports;
using System.Windows;

namespace ArctosGameServer.Communication.Protocol
{
    /// <summary>
    /// Transport Layer
    /// </summary>
    public class TransportLayer : ProtocolLayer
    {
        private readonly SerialPort serialPort;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="portName"></param>
        public TransportLayer(string portName) : base(null)
        {
            try
            {
                this.serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                this.serialPort.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("ERROR - " + ex.Message);
            }
        }

        public override PDU<object> receive()
        {
            PDU<object> pduReceived = new PDU<object>();
            if (this.serialPort.BytesToRead > 0)
            {
                char[] dataReceived = new char[128];
                this.serialPort.Read(dataReceived, 0, 128);
                pduReceived.data = dataReceived.ToString();
            }

            return pduReceived;
        }

        /// <summary>
        /// Send data via transport layer (serial) to robot
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public override bool send(PDU<object> pdu)
        {
            bool result = false;

            //PDU<string> pduInput = composePdu(pdu);
            PDU<object> pduInput = pdu;
            if (pduInput == null || pduInput.data == null) return false;

            try
            {
                this.serialPort.Write(pduInput.data.ToString());
                result = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("ERROR - " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Compose PDU from given input
        /// </summary>
        /// <param name="pduInput"></param>
        /// <returns></returns>
        protected override PDU<object> composePdu(PDU<object> pduInput)
        {
           return null;
        }

        /// <summary>
        /// Decompose PDU
        /// </summary>
        /// <param name="pduInput"></param>
        protected override PDU<object> decomposePdu(PDU<object> pduInput)
        {
            return null;
        }
    }
}
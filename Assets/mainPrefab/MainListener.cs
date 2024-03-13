using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

public class MyListener : MonoBehaviour
{
    public class BinaryArrayBuilder
    {
        private readonly MemoryStream _innerStream;

        public BinaryArrayBuilder()
        {
            _innerStream = new MemoryStream();
        }

        public BinaryArrayBuilder(byte[] initialBuffer)
        {
            _innerStream = new MemoryStream(initialBuffer);
        }

        public void AppendByte(byte value)
        {
            _innerStream.WriteByte(value);
        }

        public void AppendBytes(byte[] values)
        {
            _innerStream.Write(values);
        }

        public void AppendValues(string format, params object[] values)
        {
            AppendBytes(StructPacker.Pack(format, values));
        }

        public byte[] ToArray() => _innerStream.ToArray();
    }

    public static class StructPacker
    {
        /// <summary>
        /// Packs the values according to the provided format
        /// </summary>
        /// <param name="format">Format matching Python's struct.pack: https://docs.python.org/3/library/struct.html</param>
        /// <param name="values">Values to pack</param>
        /// <returns>Byte array containing packed values</returns>
        /// <exception cref="InvalidOperationException">Thrown when values array doesn't have enough entries to match the format</exception>
        public static byte[] Pack(string format, params object[] values)
        {
            var builder = new BinaryArrayBuilder();
            var littleEndian = true;
            var valueCtr = 0;
            foreach (var ch in format)
            {
                if (ch == '<')
                {
                    littleEndian = true;
                }
                else if (ch == '>')
                {
                    littleEndian = false;
                }
                else if (ch == 'x')
                {
                    builder.AppendByte(0x00);
                }
                else
                {
                    if (valueCtr >= values.Length)
                        throw new InvalidOperationException("Provided too little values for given format string");

                    var (formatType, _) = GetFormatType(ch);
                    var value = Convert.ChangeType(values[valueCtr], formatType);
                    var bytes = TypeAgnosticGetBytes(value);
                    var endianFlip = littleEndian != BitConverter.IsLittleEndian;
                    if (endianFlip)
                        bytes = (byte[])bytes.Reverse();

                    builder.AppendBytes(bytes);

                    valueCtr++;
                }
            }

            return builder.ToArray();
        }

        /// <summary>
        /// Unpacks data from byte array to tuple according to format provided
        /// </summary>
        /// <typeparam name="T">Tuple type to return values in</typeparam>
        /// <param name="data">Bytes that should contain your values</param>
        /// <returns>Tuple containing unpacked values</returns>
        /// <exception cref="InvalidOperationException">Thrown when values array doesn't have enough entries to match the format</exception>
        public static T Unpack<T>(string format, byte[] data)
            where T : ITuple
        {
            List<object> resultingValues = new List<object>();
            var littleEndian = true;
            var valueCtr = 0;
            var dataIx = 0;
            var tupleType = typeof(T);
            foreach (var ch in format)
            {
                if (ch == '<')
                {
                    littleEndian = true;
                }
                else if (ch == '>')
                {
                    littleEndian = false;
                }
                else if (ch == 'x')
                {
                    dataIx++;
                }
                else
                {
                    if (valueCtr >= tupleType.GenericTypeArguments.Length)
                        throw new InvalidOperationException("Provided too little tuple arguments for given format string");

                    var (formatType, formatSize) = GetFormatType(ch);

                    var valueBytes = data[dataIx..(dataIx + formatSize)];
                    var endianFlip = littleEndian != BitConverter.IsLittleEndian;
                    if (endianFlip)
                        valueBytes = (byte[])valueBytes.Reverse();

                    var value = TypeAgnosticGetValue(formatType, valueBytes);

                    var genericType = tupleType.GenericTypeArguments[valueCtr];
                    if (genericType == typeof(bool))
                        resultingValues.Add(value);
                    else
                        resultingValues.Add(Convert.ChangeType(value, genericType));

                    valueCtr++;
                    dataIx += formatSize;
                }
            }

            if (resultingValues.Count != tupleType.GenericTypeArguments.Length)
                throw new InvalidOperationException("Mismatch between generic argument count and pack format");

            var constructor = tupleType.GetConstructor(tupleType.GenericTypeArguments);
            return (T)constructor!.Invoke(resultingValues.ToArray());
        }

        /// <summary>
        /// Used to unpack single value from byte array. Shorthand to not have to declare and deconstruct tuple in your code
        /// </summary>
        /// <typeparam name="TValue">Type of value you need</typeparam>
        /// <param name="data">Bytes that should contain your values</param>
        /// <returns>Value unpacked from data</returns>
        /// <exception cref="InvalidOperationException">Thrown when values array doesn't have enough entries to match the format</exception>
        public static TValue UnpackSingle<TValue>(string format, byte[] data)
        {
            var templateTuple = new ValueTuple<TValue>(default!);
            var unpackResult = Unpack(templateTuple, format, data);
            return unpackResult.Item1;
        }

        /// <summary>
        /// Workaround for language limitations XD Couldn't call Unpack<(T value)>(format, data) in UnpackSingle
        /// </summary>
        private static T Unpack<T>(T _, string format, byte[] data)
            where T : ITuple
        {
            return Unpack<T>(format, data);
        }

        private static (Type type, int size) GetFormatType(char formatChar)
        {
            return formatChar switch
            {
                'i' => (typeof(int), sizeof(int)),
                'I' => (typeof(uint), sizeof(uint)),
                'q' => (typeof(long), sizeof(long)),
                'Q' => (typeof(ulong), sizeof(ulong)),
                'h' => (typeof(short), sizeof(short)),
                'H' => (typeof(ushort), sizeof(ushort)),
                'b' => (typeof(sbyte), sizeof(sbyte)),
                'B' => (typeof(byte), sizeof(byte)),
                '?' => (typeof(bool), 1),
                _ => throw new InvalidOperationException("Unknown format char"),
            };
        }

        // We use this function to provide an easier way to type-agnostically call the GetBytes method of the BitConverter class.
        // This means we can have much cleaner code below.
        private static byte[] TypeAgnosticGetBytes(object o)
        {
            if (o is bool b) return b ? new byte[] { 0x01 } : new byte[] { 0x00 };
            if (o is int x) return BitConverter.GetBytes(x);
            if (o is uint x2) return BitConverter.GetBytes(x2);
            if (o is long x3) return BitConverter.GetBytes(x3);
            if (o is ulong x4) return BitConverter.GetBytes(x4);
            if (o is short x5) return BitConverter.GetBytes(x5);
            if (o is ushort x6) return BitConverter.GetBytes(x6);
            if (o is byte || o is sbyte) return new byte[] { (byte)o };
            throw new ArgumentException("Unsupported object type found");
        }

        private static object TypeAgnosticGetValue(Type type, byte[] data)
        {
            if (type == typeof(bool)) return data[0] > 0;
            if (type == typeof(int)) return BitConverter.ToInt32(data, 0);
            if (type == typeof(uint)) return BitConverter.ToUInt32(data, 0);
            if (type == typeof(long)) return BitConverter.ToInt64(data, 0);
            if (type == typeof(ulong)) return BitConverter.ToUInt64(data, 0);
            if (type == typeof(short)) return BitConverter.ToInt16(data, 0);
            if (type == typeof(ushort)) return BitConverter.ToUInt16(data, 0);
            if (type == typeof(byte)) return data[0];
            if (type == typeof(sbyte)) return (sbyte)data[0];
            throw new ArgumentException("Unsupported object type found");
        }
    }

    Thread thread;
    public int connectionPort = 13000;
    TcpListener server;
    TcpClient client;
    bool running;
    public static bool b1;
    public static int pos1;
    public static bool b2;
    public static int pos2;


    void Start()
    {
        // Receive on a separate thread so Unity doesn't freeze waiting for data
        ThreadStart ts = new ThreadStart(GetData);
        thread = new Thread(ts);
        thread.Start();
    }

    void GetData()
    {
        IPHostEntry ipHost = Dns.GetHostEntry(Dns.GetHostName());
        IPAddress ipAddr = IPAddress.Parse("13.43.110.192");
        IPEndPoint localEndPoint = new IPEndPoint(ipAddr, 13000);

        // Creation TCP/IP Socket using 
        // Socket Class Constructor
        Socket sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        sender.Connect(localEndPoint);

        // We print EndPoint information 
        // that we are connected

        Debug.Log("Socket connected to -> {0} ");

        byte[] PmessageSent = Encoding.ASCII.GetBytes(TCPStart.playerno.ToString());
        int PbyteSent = sender.Send(PmessageSent);
        byte[] PreceivedBytes = new byte[1024];
        int PbytesRead = sender.Receive(PreceivedBytes);

        while (!GameManager.EndGame)
        {

            // we will send to Server
            if (TCPStart.playerno == 1)
            {
                byte[] messageSent = Encoding.ASCII.GetBytes(P1scoreboardScript.P1score.ToString());
                int byteSent = sender.Send(messageSent);
            } else if(TCPStart.playerno == 2)
            {
                byte[] messageSent = Encoding.ASCII.GetBytes(P2scoreboardScript.P2score.ToString());
                int byteSent = sender.Send(messageSent);
            }
            

            // Data buffer
            byte[] messageReceived = new byte[1024];
            int byteRecv = sender.Receive(messageReceived);
            var data = StructPacker.Unpack<(bool p1, int pos1, bool p2, int pos2, int oscore)>("<?h?hh", messageReceived);
            b1 = data.p1;
            pos1 = data.pos1;
            b2 = data.p2;
            pos2 = data.pos2;
            if (TCPStart.playerno == 1)
            {
                P2scoreboardScript.P2score = (float)data.oscore;
            }
            else if (TCPStart.playerno == 2)
            {
                P1scoreboardScript.P1score = (float)data.oscore;
            }
        }
        // Close Socket using 
        // the method Close()
        byte[] endSent = Encoding.ASCII.GetBytes("end");
        int endbyteSent = sender.Send(endSent);
        sender.Shutdown(SocketShutdown.Both);
        sender.Close();
        GameManager.EndGame = false;
    }

}
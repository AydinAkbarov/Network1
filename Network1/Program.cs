using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;

namespace Client
{
    class Program
    {
        private const int MaxUdpPayload = 60000;
        private const int HeaderSize = 12;
        private const int MaxDataSize = MaxUdpPayload - HeaderSize;

        [STAThread]
        static void Main()
        {
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            var ip = IPAddress.Parse("127.0.0.1");
            var port = 4567;
            var remoteEP = new IPEndPoint(ip, port);

            Console.WriteLine("Type a message and press Enter. If you type 'screenshot', a screenshot will be sent:");

            while (true)
            {
                var msg = Console.ReadLine() ?? "";

                if (msg.ToLower() == "screenshot")
                {
                    try
                    {
                        byte[] screenshotBytes = CaptureScreen();

                        if (screenshotBytes.Length > 10 * 1024 * 1024)
                        {
                            Console.WriteLine("Screenshot is larger than 10MB, sending cancelled.");
                            continue;
                        }

                        SendScreenshotInChunks(client, remoteEP, screenshotBytes);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error capturing or sending screenshot: {ex.Message}");
                    }
                }
                else
                {
                    var buffer = Encoding.UTF8.GetBytes(msg);

                    var typeHeader = BitConverter.GetBytes(0);

                    byte[] data = new byte[typeHeader.Length + buffer.Length];
                    Buffer.BlockCopy(typeHeader, 0, data, 0, typeHeader.Length);
                    Buffer.BlockCopy(buffer, 0, data, typeHeader.Length, buffer.Length);

                    if (data.Length > MaxUdpPayload)
                    {
                        Console.WriteLine("Text message is too long, could not be sent.");
                        continue;
                    }

                    client.SendTo(data, remoteEP);
                    Console.WriteLine($"Message sent to server: {msg}");
                }
            }
        }

        static void SendScreenshotInChunks(Socket client, EndPoint remoteEP, byte[] imageBytes)
        {
            int totalChunks = (int)Math.Ceiling((double)imageBytes.Length / MaxDataSize);

            Console.WriteLine($"Screenshot is {imageBytes.Length} bytes. Splitting into {totalChunks} chunks...");

            byte[] typeHeader = BitConverter.GetBytes(1);
            byte[] totalChunksHeader = BitConverter.GetBytes(totalChunks);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * MaxDataSize;
                int sizeToSend = Math.Min(MaxDataSize, imageBytes.Length - offset);

                byte[] packet = new byte[HeaderSize + sizeToSend];

                byte[] currentChunkHeader = BitConverter.GetBytes(i);

                Buffer.BlockCopy(typeHeader, 0, packet, 0, 4);
                Buffer.BlockCopy(totalChunksHeader, 0, packet, 4, 4);
                Buffer.BlockCopy(currentChunkHeader, 0, packet, 8, 4);

                Buffer.BlockCopy(imageBytes, offset, packet, HeaderSize, sizeToSend);

                client.SendTo(packet, remoteEP);
                Console.Write($"\rChunk {i + 1}/{totalChunks} sent. ");
            }
            Console.WriteLine("\nScreenshot successfully sent to the server.");
        }

        static byte[] CaptureScreen()
        {
            int width = Screen.PrimaryScreen.Bounds.Width;
            int height = Screen.PrimaryScreen.Bounds.Height;

            using (Bitmap bmp = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(bmp))
            using (MemoryStream ms = new MemoryStream())
            {
                g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                bmp.Save(ms, ImageFormat.Jpeg);
                return ms.ToArray();
            }
        }
    }
}
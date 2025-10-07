using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        private const int HeaderSize = 12;
        private const int MaxUdpPayload = 60000;
        private static readonly string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        private static Dictionary<string, (int TotalChunks, Dictionary<int, byte[]> Chunks)> incomingImages =
            new Dictionary<string, (int TotalChunks, Dictionary<int, byte[]> Chunks)>();

        static void Main(string[] args)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var ip = IPAddress.Parse("127.0.0.1");
            var port = 4567;
            var listenerEP = new IPEndPoint(ip, port);

            try
            {
                listener.Bind(listenerEP);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket binding error: {ex.Message}");
                return;
            }

            var buffer = new byte[MaxUdpPayload];
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0) as EndPoint;

            Console.WriteLine($"Server running on {ip}:{port}. Screenshots will be saved on Desktop.");

            while (true)
            {
                try
                {
                    int receivedBytes = listener.ReceiveFrom(buffer, ref remoteEndPoint);

                    byte[] data = new byte[receivedBytes];
                    Array.Copy(buffer, data, receivedBytes);

                    Task.Run(() => ProcessData(data, remoteEndPoint));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                }
            }
        }

        private static void ProcessData(byte[] data, EndPoint senderEP)
        {
            var senderKey = senderEP.ToString();
            if (data.Length < 4) return;
            int dataType = BitConverter.ToInt32(data, 0);

            if (dataType == 0)
            {
                var message = Encoding.Default.GetString(data, 4, data.Length - 4);
                Console.WriteLine($"\n--- MESSAGE ({senderKey}) ---\n{message}");
            }
            else if (dataType == 1 && data.Length >= HeaderSize)
            {
                int totalChunks = BitConverter.ToInt32(data, 4);
                int chunkIndex = BitConverter.ToInt32(data, 8);

                byte[] chunkData = new byte[data.Length - HeaderSize];
                Array.Copy(data, HeaderSize, chunkData, 0, chunkData.Length);

                lock (incomingImages)
                {
                    if (!incomingImages.ContainsKey(senderKey))
                    {
                        incomingImages[senderKey] = (totalChunks, new Dictionary<int, byte[]>());
                    }

                    var entry = incomingImages[senderKey];
                    if (!entry.Chunks.ContainsKey(chunkIndex))
                    {
                        entry.Chunks.Add(chunkIndex, chunkData);
                    }

                    Console.Write($"Received chunk: {entry.Chunks.Count}/{totalChunks} ({senderKey}) - Current chunk: {chunkIndex}");

                    if (entry.Chunks.Count == totalChunks)
                    {
                        FinalizeImage(senderKey, entry.Chunks);
                        incomingImages.Remove(senderKey);
                    }
                }
            }
        }

        private static void FinalizeImage(string senderKey, Dictionary<int, byte[]> chunks)
        {
            Console.WriteLine($"\nAll chunks received ({senderKey}). Reconstructing image...");

            var sortedChunks = chunks.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
            int totalLength = sortedChunks.Sum(c => c.Length);
            byte[] finalImageBytes = new byte[totalLength];
            int offset = 0;

            foreach (var chunk in sortedChunks)
            {
                Buffer.BlockCopy(chunk, 0, finalImageBytes, offset, chunk.Length);
                offset += chunk.Length;
            }

            string safeSender = senderKey.Replace(':', '_');
            string clientFolder = Path.Combine(DesktopPath, safeSender);
            Directory.CreateDirectory(clientFolder);

            string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            string fullPath = Path.Combine(clientFolder, fileName);

            try
            {
                File.WriteAllBytes(fullPath, finalImageBytes);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n!!! SCREENSHOT SUCCESSFULLY SAVED: {fullPath}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n!!! ERROR: Could not save file: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
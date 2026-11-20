using System;
using System.IO;
using System.IO.Ports;
using System.Management; // Kræver System.Management NuGet-pakken
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SerialNumberScanner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Søger efter tilsluttede ATmega328PB Xplained Mini boards...");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ScanWindowsPorts();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ScanLinuxPorts();
            }
            else
            {
                Console.WriteLine("Understøttet operativsystem ikke fundet.");
            }

            Console.WriteLine("\nSøgning færdig. Tryk på en tast for at afslutte.");
            Console.ReadKey();
        }

        // --- WINDOWS LØSNING (Bruger WMI) ---
        private static void ScanWindowsPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                string serialNumber = GetWindowsUsbSerialNumber(port);
                if (!string.IsNullOrEmpty(serialNumber) && serialNumber.StartsWith("ATML"))
                {
                    Console.WriteLine($"[Windows] Fundet! Port: {port} | Serienummer: {serialNumber}");
                }
            }
        }

        //private static string GetWindowsUsbSerialNumber(string portName)
        //{
        //    try
        //    {
        //        // Kræver System.Management NuGet-pakken (kun relevant på Windows)
        //        string query = $"SELECT * FROM Win32_SerialPort WHERE DeviceID='{portName}'";
        //        using (var searcher = new System.Management.ManagementObjectSearcher(query))
        //        {
        //            foreach (var queryObj in searcher.Get())
        //            {
        //                string pnpDeviceId = queryObj["PNPDeviceID"]?.ToString();
        //                if (!string.IsNullOrEmpty(pnpDeviceId))
        //                {
        //                    string[] parts = pnpDeviceId.Split('\\');
        //                    if (parts.Length >= 3)
        //                    {
        //                        string rawSerial = parts[2];
        //                        if (rawSerial.Contains("&"))
        //                        {
        //                            rawSerial = rawSerial.Split('&')[0];
        //                        }
        //                        return rawSerial;
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Fejl på port {portName}: {ex.Message}");
        //    }
        //    return null;
        //}

        //private static string? GetWindowsUsbSerialNumber(string portName)
        //{
        //    try
        //    {
        //        // Find PnP-enheden der matcher f.eks. "(COM15)" i navnet
        //        string query = $"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%({portName})%'";
        //        using var searcher = new ManagementObjectSearcher(query);

        //        foreach (ManagementObject obj in searcher.Get())
        //        {
        //            string? name = obj["Name"]?.ToString();
        //            string? pnpId = obj["PNPDeviceID"]?.ToString();

        //            Console.WriteLine($"[Debug {portName}] Name: {name} | PNPDeviceID: {pnpId}");

        //            if (!string.IsNullOrEmpty(pnpId))
        //            {
        //                // Kig efter et Atmel/Microchip serienummer (typisk ATML efterfulgt af alfanumeriske tegn)
        //                Match match = Regex.Match(pnpId, @"ATML[A-Z0-9]+", RegexOptions.IgnoreCase);
        //                if (match.Success)
        //                {
        //                    return match.Value.ToUpperInvariant();
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Fejl ved WMI-opslag for {portName}: {ex.Message}");
        //    }

        //    return null;
        //}

        private static string? GetWindowsUsbSerialNumber(string portName)
        {
            try
            {
                string query = $"SELECT PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%({portName})%'";
                using var searcher = new ManagementObjectSearcher(query);

                foreach (ManagementObject obj in searcher.Get())
                {
                    string? currentDeviceId = obj["PNPDeviceID"]?.ToString();

                    while (!string.IsNullOrEmpty(currentDeviceId))
                    {
                        Match match = Regex.Match(currentDeviceId, @"ATML[A-Z0-9]+", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return match.Value.ToUpperInvariant();
                        }

                        currentDeviceId = GetParentDeviceIdNative(currentDeviceId);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fejl ved opslag for {portName}: {ex.Message}");
            }

            return null;
        }

        private static string? GetParentDeviceIdNative(string deviceId)
        {
            if (CM_Locate_DevNode(out uint devInst, deviceId, CM_LOCATE_DEVNODE_NORMAL) != 0)
                return null;

            if (CM_Get_Parent(out uint parentInst, devInst, 0) != 0)
                return null;

            var sb = new StringBuilder(260);
            if (CM_Get_Device_ID(parentInst, sb, sb.Capacity, 0) != 0)
                return null;

            return sb.ToString();
        }

        private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNode(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_ID(uint dnDevInst, StringBuilder buffer, int bufferLen, uint ulFlags);
        // --- LINUX LØSNING (Bruger /dev/serial/by-id/) ---
        private static void ScanLinuxPorts()
        {
            string byIdPath = "/dev/serial/by-id";
            if (!Directory.Exists(byIdPath))
            {
                Console.WriteLine("Ingen serial by-id mappe fundet. Er enheden tilsluttet?");
                return;
            }

            string[] devices = Directory.GetFiles(byIdPath);
            foreach (string device in devices)
            {
                // Eksempel på filnavn: usb-Atmel_Corp_ATmega328PB_Xplained_Mini_ATML2523052700010973-if00
                string fileName = Path.GetFileName(device);

                if (fileName.Contains("ATmega328PB") || fileName.Contains("ATML"))
                {
                    // Udtræk selve serienummeret fra filnavnet (typisk den del der starter med ATML)
                    string serialNumber = ExtractSerialFromLinuxPath(fileName);

                    // Find den faktiske tty-port (f.eks. /dev/ttyACM0) som symlinken pege på
                    string realPort = new FileInfo(device).ResolveLinkTarget(true)?.FullName ?? device;

                    Console.WriteLine($"[Linux] Fundet! Port: {realPort} | Serienummer: {serialNumber}");
                }
            }
        }

        private static string ExtractSerialFromLinuxPath(string fileName)
        {
            // Find "ATML" i filnavnet og snup resten af serienummeret (f.eks. 16 tegn)
            int index = fileName.IndexOf("ATML");
            if (index != -1 && fileName.Length >= index + 20)
            {
                return fileName.Substring(index, 16); // ATML-serienumre er 16 tegn lange
            }
            return "Ukendt";
        }
    }
}

using Harnet;
using Harnet.Dto;
using Harnet.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Harhar
{
    class Program
    {
        // Used to track unknown Media Types that were met during parsing
        static List<string> unknownMediaTypes = new List<string>();
        // Used to track connection Ids that were used (determine level 1 or level 2 URLs)
        static List<int> usedConnectionIds = new List<int>();
        static int errorCount = 0;
        static HarharLog harharLog;
        
        static List<string> urls = new List<string>();
        static List<string> actionList = new List<string>();

        static void Main(string[] args)
        {
            string[] filePaths = {};
            // Check for command-line arguments
            if (args.Length != 0)
            {
                string checkForPath = args[0];
                if (Directory.Exists(checkForPath))
                    filePaths = Directory.GetFiles(checkForPath, "*.har");
                else if (File.Exists(checkForPath))
                    filePaths = new string[] { checkForPath };
                else
                    Console.WriteLine("Error - Cannot find specified HAR file or no files were found in provided directory.");
                    Console.WriteLine("Usage: harhar.exe <HAR file or directory>");
            }
            // If no command-line arguments, look for .har files in current directory
            else
            {
                filePaths = Directory.GetFiles(@".", "*.har");
            }

            if (filePaths.Length >= 1)
            {
                DateTime date = DateTime.Now;
                string formattedDate = date.Month.ToString() + "-" + date.Day.ToString() + "-" + date.Year.ToString() + "_" + date.Hour.ToString() + "-" + date.Minute.ToString() + "-" + date.Second.ToString();
                string workingDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + formattedDate + Path.DirectorySeparatorChar;
                if (!Directory.Exists(workingDirectory))
                    Directory.CreateDirectory(workingDirectory);

                harharLog = new HarharLog(workingDirectory + "run.log");
                
                foreach (string path in filePaths)
                {
                    HandleHarFile(workingDirectory, path);
                }
                // If we encounter unknown MIME Types, write a file with their type
                if (unknownMediaTypes.Count > 0)
                {
                    string fileName = "UnknownMedia.log";
                    WriteUnknownMimeTypesToFile(workingDirectory + fileName);
                    Console.WriteLine("Unknown Media Types were found in this archive. Please send debug file " + fileName + " to arnaud.castaner@spirent.com.");
                }
            }
            else
            {
                Console.WriteLine("No HAR file found within the current directory. Please specify a file or directory holding files.");
                Console.WriteLine("Example: harhar.exe <HAR file or directory>");
            }
            
            Console.Write("Parsing complete. Press any key to exit.");
            Console.ReadKey();
        }

        private static void WriteUnknownMimeTypesToFile(string path)
        {
            File.WriteAllLines(path, unknownMediaTypes.ToArray());
        }

        private static void HandleHarFile(string workingDirectory, string filePath)
        {
            Log datLog = HarConverter.ImportHarContent(File.ReadAllText(filePath));
            Console.WriteLine("Total Response size: " + datLog.CumulatedResponseSize + " bytes (headers: " + datLog.CumulatedResponseHeaderSize + " ; bodies: " + datLog.CumulatedResponseBodySize + " )");
            Console.WriteLine("Total Request size: " + datLog.CumulatedRequestSize + " bytes (headers: " + datLog.CumulatedRequestHeaderSize + " ; bodies: " + datLog.CumulatedRequestBodySize + " )");
            Console.WriteLine("Found " + datLog.Entries.Count + " entries in log.");
            HandleLogFile(datLog, workingDirectory, Path.GetFileName(filePath));
        }

        private static async Task HandleLogEntry(Entry entry, string workingDirectory, bool firstAction)
        {
            Response resp = entry.Response;
            Request req = entry.Request;

            // If there's no file in the URL (e.g. www.fsf.org), we force it to index.html
            // We also have to add the hostname so that it's stored under the right directory (e.g. www.fsg.org\index.html) as is done for the other files
            string fileName;
            if (req.GetFileName() != null)
                fileName = req.GetFileName();
            else
                fileName = "index.html";


            // If status code is < 400 it's 200 or 300, ie: not an error
            if (resp.Status < 400)
            {
                // We keep the whole URL to build complete file path (directory + file name) but need to remove special characters and query strings not supported by the file system
                // First let's get the Request URL and filename stripped of anything special (people like to put specials characters such as ":" in their URLs)
                string cleanUrl = GetCleanUrl(req);
                string cleanFileName = EscapeSpecialCharacters(fileName);

                // Remove the filename from the URL since we save it separately
                // Since we already cleaned the URL of query parameters, we can just remove anything after the last /
                cleanUrl = StripFileNameFromUrl(cleanUrl);
                string filePathString = workingDirectory + cleanUrl + Path.DirectorySeparatorChar + cleanFileName;

                // Windows as a limitation of 248 on path name and 260 for FQP so we truncate at 248
                if (filePathString.Length >= 248)
                {
                    filePathString = filePathString.Substring(0, 248);
                    string pathLenghtWarning = "Path was too long and had to be truncated for " + filePathString;
                    errorCount++;
                    harharLog.AppendLine(pathLenghtWarning, HarharLogMessageTypes.Warning);
                }

                string storingDirectory = Path.GetDirectoryName(filePathString);
                // Sometimes the *directory* won't exist, but there will be an extensionless file (e.g.: "rsp") of the same name
                // When this happens, it passes the check below, but fails when trying to create directory so we also need a catch
                if (!Directory.Exists(storingDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(storingDirectory);
                    }
                    catch (Exception ex)
                    {
                        harharLog.AppendLine("Could not create directory " + storingDirectory + ". Caught this error: " + ex.Message, HarharLogMessageTypes.Fatal);
                        errorCount++;
                    }
                }

                WriteFile(filePathString, resp);
                actionList.Add(AddAction(entry, firstAction));
                // If this was the first action, it's no longer the case at this point
                firstAction = false;
                urls.Add(req.Url);
            }
            File.WriteAllLines(workingDirectory + "urls.txt", urls.ToArray());
            File.WriteAllLines(workingDirectory + "action_list.txt", actionList.ToArray());
        }

        private async static void HandleLogFile(Log harLog, string workingDirectory, string logFileName)
        {
            workingDirectory += logFileName + Path.DirectorySeparatorChar;            
            
            actionList.Add("### Generated on " + DateTime.Now + " by HarHar tool ###");
            
            Console.WriteLine("Creating working directory at " + workingDirectory);
            Directory.CreateDirectory(workingDirectory);

            // Used for Avalanche Action List
            bool firstAction = true;

            // Setup progress bar
            AbstractBar bar = new SwayBar();
            int end = harLog.Entries.Count;

            bar.PrintMessage("Parsing HAR file...");
            foreach (Entry entry in harLog.Entries)
            {
                await HandleLogEntry(entry, workingDirectory, firstAction);
                bar.Step();
            }
            bar.PrintMessage("Parsing completed.");
        }

        /// <summary>
        /// Writes the Avalanche actions for the Action List
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="previousConnectionId"></param>
        /// <returns></returns>
        private static string AddAction(Entry entry, bool firstAction)
        {
            string action = "";

            // First action must always be level 1
            if (firstAction)
            {
                action += "1 ";
                usedConnectionIds.Add(entry.Connection);
            }
            // If we have already seen that connection before, means it's a level 1 action (Avalanche won't open a new connection)
            else if (usedConnectionIds.Contains(entry.Connection))
            {
                action += "1 ";
            }
            // If we have never seen this connection before, means Avalanche can open a new connection for this action (level 2 action)
            else
            {
                action += "2 ";
                usedConnectionIds.Add(entry.Connection);
            }

            // Strip HTTP 1.1 headers that Avalanche adds automatically
            entry.Request.Headers.Remove("Host");
            entry.Request.Headers.Remove("Path");
            entry.Request.Headers.Remove("Version");
            entry.Request.Headers.Remove("Scheme");
            entry.Request.Headers.Remove("Method");            
            entry.Request.Headers.Remove("Connection");
            
            // Chrome adds this in SSL
            entry.Request.Headers.Remove(":host");
            entry.Request.Headers.Remove(":path");
            entry.Request.Headers.Remove(":version");
            entry.Request.Headers.Remove(":scheme");
            entry.Request.Headers.Remove(":method");

            action += entry.Request.Method + " " + entry.Request.Url;

            if (entry.Request.Headers.Count >= 1)
            {
                foreach (var pair in entry.Request.Headers)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        action += " <ADDITIONAL_HEADER=\"" + pair.Key + ": " + pair.Value[i] + "\">";
                    }
                }
            }
            return action;
        }
        private static void WriteFile(string path, Response resp)
        {
            // We default to write to text (sometimes MIME Type is omitted)
            if (resp.Content.MimeType == "" || resp.Content.MimeType == null)
            {
                string mediaTypeWarning = "Media Type not specified for " + Path.GetFileName(path) + ", will be written as text.";
                harharLog.AppendLine(mediaTypeWarning, HarharLogMessageTypes.Warning);
                errorCount++;
                resp.WriteToText(path);
            }
            else
            {
                try
                {
                    if (resp.IsText())
                    {
                        resp.WriteToText(path);
                    }
                    else
                    {
                        // Some web host return empty files...
                        if (resp.Content.Text != null && resp.IsImage())
                        {
                            resp.WriteToImage(path);
                        }
                        else
                        {
                            resp.WriteToText(path);
                        }
                    }
                }
                catch (NotImplementedException)
                {
                    unknownMediaTypes.Add(resp.Content.MimeType);
                }
                catch (Exception ex)
                {
                    string exception = "Exception caught: " + ex.Message;
                    harharLog.AppendLine(exception, HarharLogMessageTypes.Error);
                    errorCount++;
                }
            }
            
        }
        /// <summary>
        /// Returns a the Request.Url property cleaned from any HTTP prefix, query string or special characters
        /// </summary>
        /// <returns></returns>
        public static string GetCleanUrl(Request request)
        {
            string cleanString = request.StripQueryStringsFromUrl();
            cleanString = StripProtocolPrefix(cleanString);
            cleanString = StripTrailingSlash(cleanString);
            cleanString = EscapeSpecialCharacters(cleanString);
            return cleanString;
        }

        private static string EscapeSpecialCharacters(string path)
        {
            string ret = Regex.Replace(path, "[?!*<>:|]", "_", RegexOptions.None);
            return ret.ToString();
        }
        /// <summary>
        /// This method will remove protocol (http(s)://) prefix from URLs 
        /// <param name="input"></param>
        /// <returns></returns>
        private static string StripProtocolPrefix(string input)
        {
            return Regex.Replace(input, "^(http|https)://", "", RegexOptions.None);
        }

        private static string StripTrailingSlash(string input)
        {
            return input.TrimEnd('/');
        }

        /// <summary>
        /// Strips all the content after the last slash ("/") character from the provided string.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string StripFileNameFromUrl(string input)
        {
            if (input.Contains('/'))
                input = input.Substring(0, input.LastIndexOf('/'));
            return input;
        }
    }
}

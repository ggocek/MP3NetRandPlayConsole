using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MP3Sharp;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using MP3Sharp.Decoding;
using System.Reflection;
using TagLib.Xmp;
using TagLib.Id3v1;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;

namespace MP3NetRandPlayConsole
{
    class Program
    {
        public class PodFile
        {
            public string FileName { get; set; }
            public string FolderName { get; set; }

            public PodFile()
            {
                FileName = string.Empty;
                FolderName = string.Empty;
            }
        }

        public sealed class FastWaveBuffer : MemoryStream, IWaveProvider
        {
            public FastWaveBuffer(WaveFormat waveFormat, byte[] bytes) : base(bytes)
            {
                WaveFormat = waveFormat;
            }
            public FastWaveBuffer(WaveFormat waveFormat, int size = 4096) : base()
            {
                WaveFormat = waveFormat;
                Capacity = size;
            }
            public WaveFormat WaveFormat
            {
                get;
            }
        }

        /// <summary>
        /// In order to skip a duplicate performer, numFiles must be greater than this.
        /// This is an arbitrary number, assuming that's enough MP3s to have multiple performers
        /// so the program doesn't skip every other MP3.
        /// </summary>
        public static readonly int MinTotalForSkipDup = 25;
        /// <summary>
        /// The span of time during which ctrl-c clicks are counted. If the user enters ctrl-c
        /// multiple times within this time span, the program will be exited.
        /// </summary>
        public static readonly TimeSpan CancelMaxSpan = new TimeSpan(0, 1, 0);
        /// <summary>
        /// The operation code for playing MP3s (when the RELEASE EXE is used).
        /// </summary>
        public static readonly string OPERATION_PLAY = "1";
        /// <summary>
        /// The operation code for debugging MP3s (when the DEBUG EXE is used).
        /// </summary>
        public static readonly string OPERATION_DEBUG = "2";

        /// <summary>
        /// Set to true to cause the program to exit.
        /// </summary>
        public static bool StopPlay { get; set; }
        /// <summary>
        /// Set to true to cause the current song to stop and allow the program to continue.
        /// </summary>
        public static bool SkipPlay { get; set; }
        /// <summary>
        /// The number of times the user has canceled in the last minute.
        /// </summary>
        public static int CancelCount { get; set; }
        /// <summary>
        /// The number of times the user has canceled in the last minute.
        /// </summary>
        public static DateTime CancelTime { get; set; }
        /// <summary>
        /// The number of times the user clicks ctrl-c to exit the program.
        /// </summary>
        public static int CancelCountMax { get; set; }
        /// <summary>
        /// The loop index of the currently playing MP3.
        /// </summary>
        public static int PlayingIndex { get; set; }
        /// <summary>
        /// The most recently played performer, to determine if the next MP3 has the same
        /// performer and should be skipped.
        /// </summary>
        public static bool LastPerformerDupSkipped { get; set; }
        /// <summary>
        /// A flag indicating that the performer is the same as the previous performer
        /// and should be skipped.
        /// </summary>
        public static bool SkipDup { get; set; }

        /// <summary>
        /// The main entry point for MP3NetRandPlayConsole.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // If we get this far, there is one arg and it is a folder name.
            string sourceFolder = string.Empty;
            int numFiles = 0;
            string fileExtension = ".mp3";
            string operation = OPERATION_PLAY;

            try
            {
                bool validInput = ValidateArgs(args);
                if (!validInput)
                    return;
                if (File.Exists(args[0]))
                {
                    sourceFolder = new FileInfo(args[0]).DirectoryName;
                }
                else if (Directory.Exists(args[0]))
                {
                    sourceFolder = args[0];
                }
                else
                {
                    throw new Exception("No folder or file was found matching the supplied argument.");
                }
                if ((operation != "1") && (operation != "2") && (operation != "3"))
                    throw new Exception("MP3NetRandPlayConsole operation must be 1 (write a PLS file with random selections) or 2 (play random selections) or 3 (test the MP3 files)");

#if DEBUG
                operation = OPERATION_DEBUG;
                numFiles = 1; // All files under the folder will be debugged.
#else
                operation = OPERATION_PLAY;
                // For now, no way for the user to change the number of files to play.
                // numFiles will be set to the number of MP3s actually found.
                numFiles = int.MaxValue;
#endif
                // Get the initial list of all files with the requested extension, filtered on the mod date if desired.
                List<PodFile> podFiles = SourceFiles(sourceFolder, numFiles, fileExtension, operation);

                // Set numFiles to the to the total number of files, each file will be played once in random order.
                // In the future, this could be used to play fewer than the total, or to repeat plays.
                // If debugging, this is ignored, just debug each file once.
                if (numFiles > podFiles.Count)
                    numFiles = podFiles.Count;
                List<PodFile> randPodFiles = new List<PodFile>();
                if (podFiles.Count > MinTotalForSkipDup)
                {
                    // There are enough files to allow random play.
                    randPodFiles = RandFiles(podFiles, numFiles);
                }
                else if (podFiles.Count > 0)
                {
                    // There are files, but not enjough to allow random play. Play them in order,
                    // such as when playing a single CD.
                    randPodFiles = podFiles;
                }
                else
                {
                    throw new Exception("No files found under " + sourceFolder + " with extension " + fileExtension);
                }

                if (operation == OPERATION_PLAY)
                {
                    // Catch Ctrl-C to skip the current song or cancel the program.
                    Console.CancelKeyPress += new ConsoleCancelEventHandler(Mp3PlayCancelHandler);

                    StopPlay = false;
                    SkipPlay = false;
                    CancelCountMax = 5;
                    CancelTime = DateTime.Now;
                    CancelCount = 0;
                    PlayingIndex = 0;
                    LastPerformerDupSkipped = false;
                    SkipDup = false;
                    string[] prevPerformers = new string[0] { };
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        if ((operation == OPERATION_PLAY) && (podFiles.Count > MinTotalForSkipDup))
                        {
                            Console.WriteLine(podFiles.Count.ToString() + " MP3 files, playing in random order.");
                        }
                        else if (operation == OPERATION_PLAY)
                        {
                            Console.WriteLine(podFiles.Count.ToString() + " MP3 files, playing in alphabetical order by file name.");
                        }
                        else
                        {
                            Console.WriteLine(podFiles.Count.ToString() + " MP3 files, debugging in alphabetical order by file name.");
                        }
                        Console.ResetColor();
                        foreach (PodFile pf in randPodFiles)
                        {
                            // Watch for a skip or stop.
                            if (StopPlay)
                            {
                                StopPlay = false;
                                break;
                            }
                            else if (SkipPlay)
                            {
                                StopPlay = false;
                                SkipPlay = false;
                                continue;
                            }

                            // pf.FileName has the full path and name, but there may be spaces
                            FileInfo myFile = new FileInfo(pf.FileName);
                            PlayingIndex++;
                            // Get the ID3 duration tag from the MP3 file, for display purposes.
                            // Determine if the previous artist was just played.
                            // If so, skip the artist, then reset the flag so if the artist comes up again, play the artist.
                            // The reset the flag so the artist can be skipped once again. Etc.
                            using (TagLib.File mp3Tag = TagLib.File.Create(myFile.FullName))
                            {
                                try
                                {
                                    if ((MinTotalForSkipDup < numFiles) && !LastPerformerDupSkipped)
                                    {
                                        if ((mp3Tag.Tag.Performers.Length > 0) &&
                                            (prevPerformers.Length > 0))
                                        {
                                            SkipDup = false;
                                            foreach (string prv in prevPerformers)
                                            {
                                                foreach (string cur in mp3Tag.Tag.Performers)
                                                {
                                                    if (cur == prv)
                                                    {
                                                        SkipDup = true;
                                                        // If the artist is about to be skipped,
                                                        // clear the list so the artist is not skipped next time.
                                                        prevPerformers = new string[0] { };
                                                        break;
                                                    }
                                                }
                                            }
                                            if (!SkipDup)
                                            {
                                                // If the artist is about to be played,
                                                // remember the artist to check the next time around.
                                                prevPerformers = mp3Tag.Tag.Performers;
                                            }
                                        }
                                        else
                                        {
                                            // Not skipping the current song
                                            prevPerformers = mp3Tag.Tag.Performers;
                                        }
                                    }
                                    else
                                    {
                                        // Not skipping the current song
                                        prevPerformers = mp3Tag.Tag.Performers;
                                        if (LastPerformerDupSkipped)
                                        {
                                            LastPerformerDupSkipped = false;
                                            SkipDup = false;
                                        }
                                    }
                                    Mp3PlayShowInfo(numFiles, myFile.FullName, mp3Tag);
                                }
                                catch { }
                            }

                            if (SkipDup)
                            {
                                Console.WriteLine(Environment.NewLine + "SKIPPED DUPLICATE ARTIST");
                                LastPerformerDupSkipped = true;
                            }
                            else
                            {
                                LastPerformerDupSkipped = false;
                                Mp3Play(myFile.FullName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(Environment.NewLine + "*** ERROR ***" + Environment.NewLine +
                            ex.Message + Environment.NewLine + "*** ERROR ***");
                    }
                }

                if (operation == OPERATION_DEBUG)
                {
                    int totalSoFar = 0;
                    int totalErrors = 0;
                    try
                    {
                        Console.WriteLine("Testing " + podFiles.Count.ToString() + " MP3 files at " + DateTime.Now.ToString());
                        Console.WriteLine();
                        foreach (PodFile pf in podFiles)
                        {
                            FileInfo myFile = new FileInfo(pf.FileName);
                            using (TagLib.File mp3Tag = TagLib.File.Create(myFile.FullName))
                            {
                                int err = Mp3Test(myFile.FullName, ++totalSoFar, mp3Tag.Properties.Duration);
                                Console.WriteLine();
                                totalErrors += err;
                            }
                        }
                        Console.WriteLine(totalErrors + " MP3 files in error at " + DateTime.Now.ToString());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// One arg, either /h or /?, or a string string source folder useed to find MP3 files.
        /// </summary>
        /// <param name="inputArgs">The program's input args, should be one.</param>
        /// <returns>True if valid else false</returns>
        private static bool ValidateArgs(string[] inputArgs)
        {
            try
            {
                if (inputArgs.Length != 1)
                {
                    throw new Exception(Environment.NewLine + "USAGE:" + Environment.NewLine +
                        "    MP3NetRandPlayConsole [[drive:][path] | /? | /H ]" + Environment.NewLine +
                        "    The program processes all MP3 files under the path (folders and subfolders)." + Environment.NewLine +
                        "    MP3NetRandPlayConsole uses only the console window (no custom windows)." + Environment.NewLine +
                        "    For more details, use a help option (/? or /h).");
                }
                if ((inputArgs[0] == "/?") || (inputArgs[0] == "/h") || (inputArgs[0] == "/H"))
                {
#if DEBUG
                    Help(true);
#else
                    Help(false);
#endif
                    return false;
                }
                if (!Directory.Exists(inputArgs[0]) && !File.Exists(inputArgs[0]))
                {
                    // The input arg is not an existing directory nor file
                    throw new Exception(Environment.NewLine + "USAGE:" + Environment.NewLine +
                        "    MP3NetRandPlayConsole [[drive:][path] | /? | /H ]" + Environment.NewLine +
                        "    No folder or file was found matching the supplied argument.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Display the help file for the current culture. If no such file exists, display English.
        /// </summary>
        /// <param name="debugMode">Set by the caller via compiler variables.</param>
        public static void Help(bool debugMode)
        {
            CultureInfo ci = CultureInfo.InstalledUICulture;
            string helpFile = string.Empty;
            string helpPath = null;
            string[] lines = null;

            if (debugMode)
            {
                helpFile = @"Help\help-debug-" + ci.Name + ".txt";
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), helpFile)))
                {
                    helpFile = @"Help\help-debug-en-US.txt";
                }
            }
            else
            {
                helpFile = @"Help\help-release-" + ci.Name + ".txt";
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), helpFile)))
                {
                    helpFile = @"Help\help-release-en-US.txt";
                }
            }
            helpPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), helpFile);
            lines = File.ReadAllLines(helpPath);
            foreach (string line in lines)
            {
                Console.WriteLine(line);
            }
        }

        /// <summary>
        /// Find all the MP3 files.
        /// </summary>
        /// <param name="parentFolder">The top level folder in which to search.</param>
        /// <param name="myNumFiles">The number of files to process.</param>
        /// <param name="myFileExtension">The file extension to find, usually MP3.</param>
        /// <param name="operation">OPERATION_DEBUG for debugging files, OPERATION_PLAY for playing files.</param>
        /// <returns>The list of PodFile instances.</returns>
        private static List<PodFile> SourceFiles(string parentFolder, int myNumFiles,
            string myFileExtension, string operation)
        {
            List<PodFile> allPodFiles = new List<PodFile>();
            // Get an enumerable list of directory names.
            // Start with parentFolder because the EnumerateDirectories result does not include the path argument itself.
            IEnumerable<string> parentIenum = new List<string> { parentFolder } as IEnumerable<string>;
            // Get the child directories of EnumerateDirectories.
            IEnumerable<string> childIenum = Directory.EnumerateDirectories(Path.Combine(parentFolder), "*", SearchOption.AllDirectories);
            // Concatenate the two enumerables.
            childIenum = parentIenum.Union(childIenum);

            // Traverse the folders and collect the relevant files in each folder.
            // The recursion has already been done by the call to EnumerateDirectories.
            // Just iterate and get the files in each folder.
            foreach (string childFolder in childIenum)
            {
                IEnumerable<string> childFiles = Directory.EnumerateFiles(childFolder, "*" + myFileExtension, SearchOption.TopDirectoryOnly);

                foreach (string childFile in childFiles)
                {
                    // In each subfolder, collect every file
                    allPodFiles.Add(new PodFile()
                    {
                        FileName = childFile,
                        FolderName = childFolder
                    });
                }
            }

            return allPodFiles;
        }

        /// <summary>
        /// Load a small list of PodFile instances from a random selection of all PodFile instances. 
        /// </summary>
        /// <param name="podFiles">The list of all PodFile instances.</param>
        /// <param name="numFiles">The number of instnaces to return.</param>
        /// <returns>The list of numFiles PodFile instances.</returns>
        private static List<PodFile> RandFiles(List<PodFile> podFiles, int numFiles)
        {
            List<PodFile> retPFiles = new List<PodFile>();
            int loopCnt = 0;
            Random iFile = new Random(DateTime.Now.Millisecond);
            List<int> usedIndices = new List<int>();
            do
            {
                // Iterate through random integers until the desired number have been collected
                int index = iFile.Next(0, podFiles.Count);
                if (usedIndices.Contains(index))
                {
                    // Don't duplicate
                    loopCnt++;
                    continue;
                }
                usedIndices.Add(index);
                // Save the file at that index
                retPFiles.Add(podFiles[index]);
                loopCnt++;
                // loopCnt ensures there is no infinite loop.
            } while ((retPFiles.Count < numFiles) && (loopCnt < (numFiles * 20)));

            return retPFiles;
        }

        /// <summary>
        /// Play an MP3 file and return when the song ends
        /// </summary>
        /// <param name="myFileFullName">The full path to the MP3 file to be played.</param>
        private static void Mp3Play(string myFileFullName)
        {
            try
            {
                // Load and parse MP3 file
                MP3Stream stream = null;
                FastWaveBuffer fastWaveBuffer = null;
                // Init the MP3 stream
                using (stream = new MP3Stream(myFileFullName))
                {
                    // Single channel audio (mono) such as Jack Baird Old Songs play at half speed unless the
                    // sample rate is doubled.
                    int sampleRate = (stream.ChannelCount == 1) ? stream.Frequency * 2 : stream.Frequency;
                    WaveFormat waveFormat = new WaveFormat(sampleRate, stream.ChannelCount);
                    // Allocate playback cache
                    using (fastWaveBuffer = new FastWaveBuffer(waveFormat, (int)stream.Length))
                    {
                        // Populate playback cache
                        stream.CopyTo(fastWaveBuffer);
                        fastWaveBuffer.Seek(0, SeekOrigin.Begin);
                        // Prepare player via NAudio
                        using (WaveOutEvent waveOutEvent = new WaveOutEvent())
                        {
                            waveOutEvent.Init(fastWaveBuffer);
                            waveOutEvent.Volume = 1;
                            System.Diagnostics.Stopwatch playTimer = null;
                            try
                            {
                                // Play the audio stream through the NAudio PCM device.
                                Console.ForegroundColor = ConsoleColor.Red;
                                playTimer = System.Diagnostics.Stopwatch.StartNew();
                                waveOutEvent.Play();
                                TimeSpan playSpan = TimeSpan.FromSeconds(playTimer.ElapsedMilliseconds / 1000);
                                Console.Write(string.Format("Elapsed: {0:D2}:{1:D2}:{2:D2}", playSpan.Hours, playSpan.Minutes, playSpan.Seconds));
                                Console.ResetColor();
                                // Watch for a skip or stop file in the EXE's folder.
                                while (waveOutEvent.PlaybackState == PlaybackState.Playing)
                                {
                                    if (StopPlay)
                                    {
                                        // Don't reset StopPlay/SkipPlay here. Return to get the next file and check back there.
                                        waveOutEvent.Stop();
                                    }
                                    else if (SkipPlay)
                                    {
                                        SkipPlay = false;
                                        waveOutEvent.Stop();
                                    }
                                    else
                                    {
                                        // If this sleeps for longer than 1000, then the elapsed time counter
                                        // clicks less than once per second.
                                        Thread.Sleep(1000);
                                        playSpan = TimeSpan.FromSeconds(playTimer.ElapsedMilliseconds / 1000);
                                        Console.SetCursorPosition(0, Console.CursorTop);
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.Write(string.Format("Elapsed: {0:D2}:{1:D2}:{2:D2}", playSpan.Hours, playSpan.Minutes, playSpan.Seconds));
                                        Console.ResetColor();
                                    }
                                }
                            }
                            catch
                            {
                                throw;
                            }
                            finally
                            {
                                playTimer.Stop();
                                Console.ResetColor();
                                Console.WriteLine();
                            }

                            //// Arming ManualResetEvent
                            ////using (ManualResetEvent manualResetEvent = new ManualResetEvent(false))
                            ////{
                            ////    waveOutEvent.PlaybackStopped += (object sender, StoppedEventArgs e) =>
                            ////    {
                            ////        manualResetEvent.Set();
                            ////    };
                            ////    // Done
                            ////    waveOutEvent.Play();
                            ////    manualResetEvent.WaitOne(); // returns when the song ends
                            ////}
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Show metadata about an MP3 to be played.
        /// </summary>
        /// <param name="numFiles">The total number of files.</param>
        /// <param name="myFile">The full path to the file.</param>
        /// <param name="mp3Tag">The MP3 ID3 tag object.</param>
        public static void Mp3PlayShowInfo(int numFiles, string myFile, TagLib.File mp3Tag)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.ResetColor();

            DateTime playDt = DateTime.Now;

            // Time, index, file name
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(Environment.NewLine +
                playDt.Hour.ToString("d2") + ":" + playDt.Minute.ToString("d2") + ":" + playDt.Second.ToString("d2") + " " +
                PlayingIndex.ToString() + "/" + numFiles.ToString("") + " " +
                myFile);

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Green;
            // Note
            Console.WriteLine("Ctrl-C to skip current MP3. Ctrl-C five times quickly to exit program.");

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Yellow;

            // Title            
            Console.WriteLine("Title: " + mp3Tag.Tag.Title.Trim());

            // Artist
            if (mp3Tag.Tag.Performers.Length > 0)
            {
                Console.WriteLine("Artist: " + mp3Tag.Tag.Performers[0].Trim());
            }
            else if (mp3Tag.Tag.AlbumArtists.Length > 0)
            {
                Console.WriteLine("Artist: " + mp3Tag.Tag.AlbumArtists[0].Trim());
            }
            else
            {
                Console.WriteLine("Artist: None");
            }

            // Album            
            Console.WriteLine("Album: " + mp3Tag.Tag.Album.Trim());

            // Year
            Console.WriteLine("Year: " + mp3Tag.Tag.Year.ToString());

            // Duration
            if (mp3Tag.Properties.Duration != null)
            {
                TimeSpan mp3Dur = mp3Tag.Properties.Duration;
                Console.WriteLine("Duration: " + mp3Dur.Hours.ToString("d2") + ":" + mp3Dur.Minutes.ToString("d2") + ":" + mp3Dur.Seconds.ToString("d2"));
            }
            else
            {
                Console.WriteLine("Duration: None");
            }

            Console.ResetColor();
        }

        public static void Mp3TestShowInfo(int totalSoFar, int invalidLayers, int invalidSampleRates,
            int invalidVersions, int readFullys, int zeroBytes, long testLen, long elapsedMilli,
            string exMessage, TimeSpan mp3Duration)
        {
            string dur = string.Empty;
            if (mp3Duration != null)
            {
                dur = mp3Duration.Hours.ToString("d2") + ":" + mp3Duration.Minutes.ToString("d2") + ":" + mp3Duration.Seconds.ToString("d2");
            }
            else
            {
                dur = "none";
            }

            Console.WriteLine(totalSoFar.ToString("d5") + " " + exMessage +
                ((invalidLayers >= 0) ? (" InvLyr=" + invalidLayers.ToString()) : "") +
                ((invalidSampleRates >= 0) ? (" InvSmp=" + invalidSampleRates.ToString()) : "") +
                ((invalidVersions >= 0) ? (" InvVer=" + invalidVersions.ToString()) : "") +
                ((readFullys >= 0) ? (" FullRd=" + readFullys.ToString()) : "") +
                ((zeroBytes >= 0) ? (" ZerByt=" + zeroBytes.ToString()) : "") +
                ((exMessage.EndsWith(" ") ? ("Mp3Dur=" + dur) : (" Mp3Dur=" + dur))) +
                " " + (Convert.ToDouble(testLen) / 1024D).ToString("N3") + "/" +
                    (Convert.ToDouble(elapsedMilli) / 1000D).ToString("N3") + " = " +
                    ((Convert.ToDouble(testLen) / 1024D) / (Convert.ToDouble(elapsedMilli) / 1000D)).ToString("N1") + " bps");
        }

        /// <summary>
        /// Invoked when Ctrl-C is entered into the console window.
        /// </summary>
        /// <param name="sender">The console?</param>
        /// <param name="args">Console Cancel Event Args</param>
        protected static void Mp3PlayCancelHandler(object sender, ConsoleCancelEventArgs args)
        {
            // When ctrl-c is clicked, check the cancel time. If the time is more than a minute ago,
            // reset the time to DateTime.Now and reset the count. If the time is less than a minute ago,
            // do not update the cancel time. Increment the count. If the count is less than 5,
            // set SkipPlay. If the cancel count >= 5 (multiple ctrl-c clicks within 1 minute), set StopPlay.
            // When using a CancelKeyPress handler such as this, I have not been able to figure
            // how to tell if the handler is called while processing a previous invocation (such as when
            // reading user input in the handler and the user enters ctrl-c again, re-invoking the handler).
            args.Cancel = true; // Allow playing to continue
            DateTime currentEntry = DateTime.Now;
            if ((currentEntry - CancelTime) > CancelMaxSpan)
            {
                // It has been more than a minute since the last ctrl-c (or since starting the program).
                CancelTime = currentEntry;
                CancelCount = 0;
            }
            CancelCount++;
            if (CancelCount < CancelCountMax)
            {
                // Just skip the current MP3.
                SkipPlay = true;
            }
            else
            {
                // Multiple ctrl-c clicks - exit the program.
                StopPlay = true;
            }
        }

        /// <summary>
        /// Open an MP3 stream to see if it can be submitted to the PCM audio device.
        /// If not, write a message to the console.
        /// </summary>
        /// <param name="myFileFullName">The full path to the MP3 file.</param>
        /// <param name="totalSoFar">A counter to determine the success delimiter.</param>
        /// <param name="mp3Duration">The ID3 Duration tag value.</param>
        /// <returns>0 if no error, 1 if error</returns>
        private static int Mp3Test(string myFileFullName, int totalSoFar, TimeSpan mp3Duration)
        {
            MP3Stream stream = null;
            System.Diagnostics.Stopwatch testTimer = null;
            testTimer = System.Diagnostics.Stopwatch.StartNew();
            long testLen = 0;
            bool streamErr = false;
            bool invVerWhenOk = false;
            try
            {
                // Load and parse MP3 file
                using (stream = new MP3Stream(myFileFullName))
                {
                    WaveFormat waveFormat = new WaveFormat(stream.Frequency, stream.ChannelCount);
                    // Allocate playback cache
                    using (FastWaveBuffer fastWaveBuffer = new FastWaveBuffer(waveFormat, (int)stream.Length))
                    {
                        // Populate playback cache.
                        // If CopyTo does not throw, the MP3 should play OK.
                        stream.CopyTo(fastWaveBuffer);
                        // INVALID SAMPLE RATE DETECTED or INVALID LAYER DETECTED cannot be fixed with Audacity.
                        // INVALID VERSION DETECTED can be fixed with Audacity open & export.
                        // INVALID VERSION DETECTED comes from Console.Writeline, this can be rerouted with Console.SetOut.
                        // Neither affect playback.
                        testTimer.Stop();
                        testLen = stream.Length;
                        Console.WriteLine(myFileFullName);
                        Mp3TestShowInfo(totalSoFar, stream.InvalidLayers, stream.InvalidSampleRates,
                            stream.InvalidVersions, stream.ReadFullys, stream.ZeroBytes, testLen,
                            testTimer.ElapsedMilliseconds, "ok", mp3Duration);
                        invVerWhenOk = (stream.InvalidVersions > 0);
                    }
                }
            }
            catch (ObjectDisposedException ex)
            {
                throw new IndexOutOfRangeException("ObjectDisposedException", ex);
            }
            catch (IndexOutOfRangeException ex)
            {
                testTimer.Stop();
                streamErr = CheckStream(stream);
                if ((testLen == 0) && !streamErr)
                    testLen = stream.Length;
                Console.WriteLine(myFileFullName);
                if (!streamErr)
                {
                    Mp3TestShowInfo(totalSoFar, stream.InvalidLayers, stream.InvalidSampleRates,
                        stream.InvalidVersions, stream.ReadFullys, stream.ZeroBytes, testLen,
                        testTimer.ElapsedMilliseconds, ex.Message, mp3Duration);
                }
                else
                {
                    Mp3TestShowInfo(totalSoFar, -1, -1,
                        -1, -1, -1, testLen,
                        testTimer.ElapsedMilliseconds, ex.Message + " ", mp3Duration);
                }
                return 1;
            }
            catch (Exception ex)
            {
                testTimer.Stop();
                streamErr = CheckStream(stream);
                if ((testLen == 0) && !streamErr)
                    testLen = stream.Length;
                Console.WriteLine(myFileFullName);
                if (!streamErr)
                {
                    Mp3TestShowInfo(totalSoFar, stream.InvalidLayers, stream.InvalidSampleRates,
                        stream.InvalidVersions, stream.ReadFullys, stream.ZeroBytes, testLen,
                        testTimer.ElapsedMilliseconds, ex.Message, mp3Duration);
                }
                else
                {
                    Mp3TestShowInfo(totalSoFar, -1, -1,
                        -1, -1, -1, testLen,
                        testTimer.ElapsedMilliseconds, ex.Message + " ", mp3Duration);
                }
                return 1;
            }
            if (invVerWhenOk)
            {
                return 1; // No exceptions, but there was an invalid version count which can be fixed via Audacity.
            }
            return 0; // No exceptions and no invalid versions.
        }

        /// <summary>
        /// Ensure the stream properties can be accessed.
        /// </summary>
        /// <param name="str">The MP3Stream</param>
        /// <returns>False if no exception occurs when testing the length property.</returns>
        public static bool CheckStream(MP3Stream str)
        {
            if (str == null)
            {
                return true;
            }
            try
            {
                long x = str.Length;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}

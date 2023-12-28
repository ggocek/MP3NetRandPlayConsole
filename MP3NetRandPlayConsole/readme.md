
# MP3NetRandPlayConsole - README
(c) Gary Gocek, 2023. See GitHub for licensing:
https://github.com/ggocek/MP3NetRandPlayConsole

All I wanted to do was play MP3 files from a folder structure in random
order. The obvious approach was to use Windows Media Player in shuffle
mode, but my experience was that it did not do a good job of randomly
selecting songs when the number of files was large. I have tested
MP3NetRandPlayConsole with over 8000 MP3 files in hundreds of folders,
mostly ripped from CDs, plus downloads and original recordings.
MP3NetRandPlayConsole randomizes the heck out the playlist.

I found freeware called "A Random Music Player", which randomized well
but was not able to play some of my MP3s. ARMP does not crash, but it
simply skips some of my MP3s. So, I decided to write my own software to
figure out why all this is so hard.

MP3NetRandPlayConsole uses NAudio and TagLib via NuGet with no changes.

MP3NetRandPlayConsole uses a hacked up version of Nihlus MP3Sharp.
While some of my MP3 files appeared to be not perfectly in 
conformance with MPEG-1 or MPEG-2 Audio Layer III, most MP3 players
do just fine, so in my opinion, Nihlus MP3Sharp is buggy and slow.
This is the version installed via NuGet, so I use my adjusted code
rather than the NuGet references. Details are in the Help file as
described below.

## Installation
There is no installer. Copy the DEBUG mode files and/or RELEASE mode
files and paste them onto your device, and run the EXE. This is a
console application  tested under Windows 10 and Windows 11. The source
code is C# and uses .NET Framework. As of December, 2023, the preferred
version of .NET was 4.8.1.

The app.config file has no special attributes.

The DEBUG mode EXE loads MP3 file data from a folder (recursively
collecting files from the parent and subfolders). Debugging information
is displayed. Audio is not played.

The RELEASE mode EXE plays MP3 files from a folder (recursively
collecting files from the parent and subfolders) in random order.
Metadata for the currently playing MP3 is displayed.

USAGE:
    MP3NetRandPlayConsole [[drive:][path] | /? | /H ]

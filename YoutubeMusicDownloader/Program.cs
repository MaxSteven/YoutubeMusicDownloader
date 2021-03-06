﻿using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CliWrap;
using Tyrrrz.Extensions;
using YoutubeExplode;
using YoutubeExplode.Models.MediaStreams;

namespace YoutubeMusicDownloader
{
    public class Program
    {
        private static readonly YoutubeClient YoutubeClient = new YoutubeClient();
        private static readonly Cli FfmpegCli = new Cli("ffmpeg.exe");

        private static readonly string TempDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
        private static readonly string OutputDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Output");

        private static MediaStreamInfo GetBestAudioStreamInfo(MediaStreamInfoSet set)
        {
            if (set.Audio.Any())
                return set.Audio.WithHighestBitrate();
            if (set.Muxed.Any())
                return set.Muxed.WithHighestVideoQuality();
            throw new Exception("No applicable media streams found for this video");
        }

        private static async Task DownloadAndConvertVideoAsync(string id)
        {
            Console.WriteLine($"Working on video [{id}]...");

            // Get video info
            var video = await YoutubeClient.GetVideoAsync(id);
            var set = await YoutubeClient.GetVideoMediaStreamInfosAsync(id);
            var cleanTitle = video.Title.Replace(Path.GetInvalidFileNameChars(), '_');
            Console.WriteLine($"{video.Title}");

            // Get highest bitrate audio-only or highest quality mixed stream
            var streamInfo = GetBestAudioStreamInfo(set);

            // Download to temp file
            Console.WriteLine("Downloading...");
            Directory.CreateDirectory(TempDirectoryPath);
            var streamFileExt = streamInfo.Container.GetFileExtension();
            var streamFilePath = Path.Combine(TempDirectoryPath, $"{Guid.NewGuid()}.{streamFileExt}");
            await YoutubeClient.DownloadMediaStreamAsync(streamInfo, streamFilePath);

            // Convert to mp3
            Console.WriteLine("Converting...");
            Directory.CreateDirectory(OutputDirectoryPath);
            var outputFilePath = Path.Combine(OutputDirectoryPath, $"{cleanTitle}.mp3");
            await FfmpegCli.ExecuteAsync($"-i \"{streamFilePath}\" -q:a 0 -map a \"{outputFilePath}\" -y");

            // Delete temp file
            Console.WriteLine("Deleting temp file...");
            File.Delete(streamFilePath);

            // Edit mp3 metadata
            Console.WriteLine("Writing metadata...");
            var idMatch = Regex.Match(video.Title, @"^(?<artist>.*?)-(?<title>.*?)$");
            var artist = idMatch.Groups["artist"].Value.Trim();
            var title = idMatch.Groups["title"].Value.Trim();
            using (var meta = TagLib.File.Create(outputFilePath))
            {
                meta.Tag.Performers = new[] {artist};
                meta.Tag.Title = title;
                meta.Save();
            }

            Console.WriteLine($"Downloaded and converted video [{id}] to [{outputFilePath}]");
        }

        private static async Task DownloadAndConvertPlaylistAsync(string id)
        {
            Console.WriteLine($"Working on playlist [{id}]...");

            // Get playlist info
            var playlist = await YoutubeClient.GetPlaylistAsync(id);
            Console.WriteLine($"{playlist.Title} ({playlist.Videos.Count} videos)");

            // Work on the videos
            Console.WriteLine();
            foreach (var video in playlist.Videos)
            {
                await DownloadAndConvertVideoAsync(video.Id);
                Console.WriteLine();
            }
        }

        private static async Task MainAsync(string[] args)
        {
            foreach (var arg in args)
            {
                // Try to determine the type of the URL/ID that was given

                // Playlist ID
                if (YoutubeClient.ValidatePlaylistId(arg))
                {
                    await DownloadAndConvertPlaylistAsync(arg);
                }

                // Playlist URL
                else if (YoutubeClient.TryParsePlaylistId(arg, out var playlistId))
                {
                    await DownloadAndConvertPlaylistAsync(playlistId);
                }

                // Video ID
                else if (YoutubeClient.ValidateVideoId(arg))
                {
                    await DownloadAndConvertVideoAsync(arg);
                }

                // Video URL
                else if (YoutubeClient.TryParseVideoId(arg, out var videoId))
                {
                    await DownloadAndConvertVideoAsync(videoId);
                }

                // Unknown
                else
                {
                    throw new ArgumentException($"Unrecognized URL or ID: [{arg}]", nameof(arg));
                }

                Console.WriteLine();
            }

            Console.WriteLine("Done");
        }

        public static void Main(string[] args)
        {
            Console.Title = "Youtube Music Downloader";

            MainAsync(args).GetAwaiter().GetResult();
        }
    }
}
﻿using gamevault.Models;
using gamevault.ViewModels;
using ImageMagick;
using LiveChartsCore.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace gamevault.Helper
{
    internal class CacheHelper
    {
        internal static async Task CreateOfflineCacheAsync(Game game)
        {
            try
            {
                string serializedObject = JsonSerializer.Serialize(game);
                string compressedObject = StringCompressor.CompressString(serializedObject);
                Preferences.Set(game.ID.ToString(), compressedObject, AppFilePath.OfflineCache);
            }
            catch { }
        }

        internal static async Task LoadImageCacheToUIAsync(int identifier, int imageId, string cachePath, ImageCache cacheType, System.Windows.Controls.Image img)
        {
            string cacheFile = $"{cachePath}/{identifier}.{imageId}";
            try
            {
                if (imageId == -1)
                {
                    throw new Exception("image id does not exist");
                }
                if (File.Exists(cacheFile))
                {
                    if (cacheType == ImageCache.UserAvatar)
                    {
                        if (TaskQueue.Instance.IsAlreadyInProcess(imageId))
                        {
                            await TaskQueue.Instance.WaitForProcessToFinish(imageId);
                        }
                        if (GifHelper.IsGif(cacheFile))
                        {
                            await GifHelper.LoadGif(cacheFile, img);
                            return;
                        }
                        img.BeginAnimation(System.Windows.Controls.Image.SourceProperty, null);
                    }
                    //if file exists then return it directly                   
                    img.Source = BitmapHelper.GetBitmapImage(cacheFile);
                }
                else
                {
                    if (!Directory.Exists(cachePath))
                    { Directory.CreateDirectory(cachePath); }
                    //Otherwise see if there are still cache images with the same identifier
                    string[] files = Directory.GetFiles(cachePath, $"{identifier}.*");
                    if (LoginManager.Instance.IsLoggedIn())
                    {
                        //when we are online we download the new image and delete an outdated one if available
                        if (files.Length > 0)
                        {
                            File.Delete(files[0]);
                        }
                        await TaskQueue.Instance.Enqueue(() => WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{imageId}", cacheFile), imageId);
                        if (cacheType == ImageCache.UserAvatar)
                        {
                            if (GifHelper.IsGif(cacheFile))
                            {
                                await GifHelper.LoadGif(cacheFile, img);
                                return;
                            }
                        }
                        img.Source = BitmapHelper.GetBitmapImage(cacheFile);
                    }
                    else
                    {
                        if (files.Length > 0)
                        {
                            //if we are offline, we will try to load an old image with the same identifier
                            cacheFile = files[0];
                            img.Source = BitmapHelper.GetBitmapImage(cacheFile);
                        }
                        else
                        {
                            //otherwise we load the 'No Boxart' image
                            throw new Exception();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (TaskQueue.Instance.IsAlreadyInProcess(imageId))
                    {
                        await TaskQueue.Instance.WaitForProcessToFinish(imageId);
                        img.Source = BitmapHelper.GetBitmapImage(cacheFile);
                        return;
                    }
                }
                catch { }
                img.BeginAnimation(System.Windows.Controls.Image.SourceProperty, null);//Make sure all animations are removed, so a non animated image can be set to the source
                img.Source = GetReplacementImage(cacheType);
            }
        }
        internal static async Task EnsureImageCacheForGame(Game game)
        {
            try
            {
                if (LoginManager.Instance.IsLoggedIn())
                {
                    if (TaskQueue.Instance.IsAlreadyInProcess(game.Metadata.Background.ID))
                    {
                        await TaskQueue.Instance.WaitForProcessToFinish(game.Metadata.Background.ID);
                    }
                    if (TaskQueue.Instance.IsAlreadyInProcess(game.Metadata.Cover.ID))
                    {
                        await TaskQueue.Instance.WaitForProcessToFinish(game.Metadata.Cover.ID);
                    }

                    string backGroundCacheFile = $"{AppFilePath.ImageCache}/gbg/{game.ID}.{game.Metadata.Background.ID}";
                    string boxArtCacheFile = $"{AppFilePath.ImageCache}/gbox/{game.ID}.{game.Metadata.Cover.ID}";
                    if (!Directory.Exists($"{AppFilePath.ImageCache}/gbg"))
                    {
                        Directory.CreateDirectory($"{AppFilePath.ImageCache}/gbg");
                    }
                    if (!Directory.Exists($"{AppFilePath.ImageCache}/gbox"))
                    {
                        Directory.CreateDirectory($"{AppFilePath.ImageCache}/gbox");
                    }

                    if (!File.Exists(backGroundCacheFile))
                    {
                        //Not in que because its not loading images for the UI
                        await WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{game.Metadata.Background.ID}", backGroundCacheFile);
                    }
                    if (!File.Exists(boxArtCacheFile))
                    {
                        await WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{game.Metadata.Cover.ID}", boxArtCacheFile);
                    }
                }
            }
            catch { }
        }
        internal static BitmapImage GetReplacementImage(ImageCache cacheType)
        {
            switch (cacheType)
            {
                case ImageCache.GameCover:
                    {
                        return BitmapHelper.GetBitmapImage("pack://application:,,,/gamevault;component/Resources/Images/library_NoGameCover.png");
                    }
                case ImageCache.UserAvatar:
                    {
                        return BitmapHelper.GetBitmapImage("pack://application:,,,/gamevault;component/Resources/Images/com_NoUserAvatar.png");

                    }
                default:
                    {
                        return BitmapHelper.GetBitmapImage("pack://application:,,,/gamevault;component/Resources/Images/gameView_NoBackground.jpg");
                    }
            }
        }

        internal static async Task OptimizeCache()
        {
            await Task.Run(() =>
            {
                double maxHeight = SystemParameters.FullPrimaryScreenHeight / 2;
                var files = Directory.GetFiles(AppFilePath.ImageCache, "*.*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    try
                    {
                        var image = new FileInfo(file);
                        if (image.Length > 0)
                        {
                            if (file.Contains("uico"))
                            {
                                if (GifHelper.IsGif(file))
                                {
                                    uint maxGifHeightWidth = 400;
                                    GifHelper.OptimizeGIF(file, maxGifHeightWidth);
                                    image.Refresh();
                                    continue;
                                }
                            }
                            ResizeImage(file, Convert.ToUInt32(maxHeight));
                            image.Refresh();
                        }
                        else
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            });
        }
        internal static async Task<string> CreateHashAsync(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
            {
                byte[] bytes = await sha256.ComputeHashAsync(stream);
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                string hash = builder.ToString();
                return hash;
            }
        }
        internal static Dictionary<string, string> GetImageCacheForGame(Game game)
        {
            Dictionary<string, string> imageCache = new Dictionary<string, string>();
            string cachePath = AppFilePath.ImageCache;
            var boxArt = Directory.GetFiles(Path.Combine(cachePath, "gbox").Replace("/", "\\"), $"{game.ID}.*").FirstOrDefault();
            var background = Directory.GetFiles(Path.Combine(cachePath, "gbg").Replace("/", "\\"), $"{game.ID}.*").FirstOrDefault();
            imageCache.Add("gbox", boxArt);
            imageCache.Add("gbg", background);
            return imageCache;
        }
        private static void ResizeImage(string path, uint maxHeight)
        {
            using (var imageMagick = new MagickImage(path))
            {
                imageMagick.Format = MagickFormat.Jpeg;
                if (imageMagick.Width <= imageMagick.Height && imageMagick.Height > maxHeight)
                {
                    var size = new MagickGeometry(maxHeight);
                    size.IgnoreAspectRatio = false;
                    imageMagick.Resize(size);
                    imageMagick.Write(path);
                }
                else if (imageMagick.Height > SystemParameters.FullPrimaryScreenHeight)
                {
                    var size = new MagickGeometry((uint)SystemParameters.FullPrimaryScreenWidth, (uint)SystemParameters.FullPrimaryScreenHeight);
                    size.IgnoreAspectRatio = false;
                    imageMagick.Resize(size);
                    imageMagick.Write(path);
                }
            }
        }
    }
}

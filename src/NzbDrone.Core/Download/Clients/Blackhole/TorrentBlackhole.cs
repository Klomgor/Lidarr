using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Blackhole
{
    public class TorrentBlackhole : TorrentClientBase<TorrentBlackholeSettings>
    {
        private readonly IScanWatchFolder _scanWatchFolder;

        public TimeSpan ScanGracePeriod { get; set; }

        public override bool PreferTorrentFile => true;

        public TorrentBlackhole(IScanWatchFolder scanWatchFolder,
                                ITorrentFileInfoReader torrentFileInfoReader,
                                IHttpClient httpClient,
                                IConfigService configService,
                                IDiskProvider diskProvider,
                                IRemotePathMappingService remotePathMappingService,
                                IBlocklistService blocklistService,
                                Logger logger)
            : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, blocklistService, logger)
        {
            _scanWatchFolder = scanWatchFolder;

            ScanGracePeriod = TimeSpan.FromSeconds(30);
        }

        protected override string AddFromMagnetLink(RemoteAlbum remoteAlbum, string hash, string magnetLink)
        {
            if (!Settings.SaveMagnetFiles)
            {
                throw new NotSupportedException("Blackhole does not support magnet links.");
            }

            var title = remoteAlbum.Release.Title;

            title = FileNameBuilder.CleanFileName(title);

            var filepath = Path.Combine(Settings.TorrentFolder, $"{title}.{Settings.MagnetFileExtension.Trim('.')}");

            var fileContent = Encoding.UTF8.GetBytes(magnetLink);
            using (var stream = _diskProvider.OpenWriteStream(filepath))
            {
                stream.Write(fileContent, 0, fileContent.Length);
            }

            _logger.Debug("Saving magnet link succeeded, saved to: {0}", filepath);

            return null;
        }

        protected override string AddFromTorrentFile(RemoteAlbum remoteAlbum, string hash, string filename, byte[] fileContent)
        {
            var title = remoteAlbum.Release.Title;

            title = FileNameBuilder.CleanFileName(title);

            var filepath = Path.Combine(Settings.TorrentFolder, string.Format("{0}.torrent", title));

            using (var stream = _diskProvider.OpenWriteStream(filepath))
            {
                stream.Write(fileContent, 0, fileContent.Length);
            }

            _logger.Debug("Torrent Download succeeded, saved to: {0}", filepath);

            return null;
        }

        public override string Name => "Torrent Blackhole";

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            foreach (var item in _scanWatchFolder.GetItems(Settings.WatchFolder, ScanGracePeriod))
            {
                var queueItem = new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false),
                    DownloadId = Definition.Name + "_" + item.DownloadId,
                    Category = "Lidarr",
                    Title = item.Title,

                    TotalSize = item.TotalSize,
                    RemainingTime = item.RemainingTime,

                    OutputPath = item.OutputPath,

                    Status = item.Status
                };

                queueItem.CanMoveFiles = !Settings.ReadOnly;
                queueItem.CanBeRemoved = queueItem.DownloadClientInfo.RemoveCompletedDownloads;

                yield return queueItem;
            }
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (!deleteData)
            {
                throw new NotSupportedException("Blackhole cannot remove DownloadItem without deleting the data as well, ignoring.");
            }

            DeleteItemData(item);
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(Settings.WatchFolder) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestFolder(Settings.TorrentFolder, "TorrentFolder"));
            failures.AddIfNotNull(TestFolder(Settings.WatchFolder, "WatchFolder"));
        }
    }
}

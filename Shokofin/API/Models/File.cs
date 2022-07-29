using System;
using System.Collections.Generic;

namespace Shokofin.API.Models
{
    public class File
    {
        public int ID { get; set; }
        
        public long Size { get; set; }
        
        public HashesType Hashes { get; set; }
        
        public List<Location> Locations { get; set; }
        
        public string RoundedStandardResolution { get; set; }
        
        public DateTime Created { get; set; }

        public class Location
        {
            public int ImportFolderID { get; set; }
            
            public string RelativePath { get; set; }
            
            public bool Accessible { get; set; }
        }
        
        public class HashesType
        {
            public string ED2K { get; set; }
            
            public string SHA1 { get; set; }
            
            public string CRC32 { get; set; }
            
            public string MD5 { get; set; }
        }

        /// <summary>
        /// User stats for the file.
        /// </summary>
        public class FileUserStats
        {
            /// <summary>
            /// Where to resume the next playback.
            /// </summary>
            public TimeSpan? ResumePosition { get; set; }

            /// <summary>
            /// Total number of times the file have been watched.
            /// </summary>
            public int WatchedCount { get; set; }

            /// <summary>
            /// When the file was last watched. Will be null if the full is
            /// currently marked as unwatched.
            /// </summary>
            public DateTime? LastWatchedAt { get; set; }

            /// <summary>
            /// When the entry was last updated.
            /// </summary>
            public DateTime LastUpdatedAt { get; set; }

            /// <summary>
            /// True if the <see cref="FileUserStats"/> object is considered empty.
            /// </summary>
            public virtual bool IsEmpty
            {
                get
                    => ResumePosition == null && WatchedCount == 0 && WatchedCount == 0;
            }
        }

        public class FileDetailed : File
        {
            public List<SeriesXRefs> SeriesIDs { get; set; }
            
            public class FileIDs
            {
                public int AniDB { get; set; }
                
                public List<int> TvDB { get; set; }
                
                public int ID { get; set; }
            }

            public class SeriesXRefs
            {
                public FileIDs SeriesID { get; set; }
                
                public List<FileIDs> EpisodeIDs { get; set; }
            }
        }
    }
}

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

        /// <summary>
        /// A summerised view of the user data for this file.
        /// </summary>
        public class UserDataSummary {
            /// <summary>
            /// The number of times this file have been watched. Doesn't include
            /// active watch seesions.
            /// </summary>
            public int WatchedCount;

            /// <summary>
            /// The last time this file was watched, if at all.
            /// </summary>
            public DateTime? LastWatchedAt;

            /// <summary>
            /// Number of ticks into the video to resume from. This is 0 if the video is not currently watched.
            /// </summary>
            public long ResumePositionTicks { get; set; }
        }

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

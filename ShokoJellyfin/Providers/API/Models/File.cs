using System;
using System.Collections.Generic;

namespace ShokoJellyfin.Providers.API.Models
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

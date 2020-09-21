namespace Shokofin.API.Models
{
    public abstract class BaseModel
    {
        public string Name { get; set; }
        
        public int Size { get; set; }
        
        public Sizes Sizes { get; set; }
    }
}
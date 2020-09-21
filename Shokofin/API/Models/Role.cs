namespace Shokofin.API.Models
{
    public class Role
    {
        public string Language { get; set; }
        
        public Person Staff { get; set; }
        
        public Person Character { get; set; }
        
        public string RoleName { get; set; }
        
        public string RoleDetails { get; set; }

        public class Person
        {
            public string Name { get; set; }
            
            public string AlternateName { get; set; }
            
            public string Description { get; set; }
            
            public Image Image { get; set; }
        }
    }
}
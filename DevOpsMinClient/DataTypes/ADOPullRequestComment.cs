using DevOpsMinClient.DataTypes.Details;

namespace DevOpsMinClient.DataTypes
{
    public class ADOPullRequestComment
    {
        public int Id { get; set; }
        public ADOPerson Author { get; set; }
        public string Content { get; set; }
    }
}

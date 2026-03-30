using System;

namespace RukScheduleApp.Models
{
    public class ScheduleItem
    {
        public int Id { get; set; }
        public string DayOfWeek { get; set; }
        public string Time { get; set; }
        public string Subject { get; set; }
        public string Teacher { get; set; }
        public string Room { get; set; }
        public string GroupName { get; set; }
        public DateTime Date { get; set; }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int BranchId { get; set; }
    }

    public class Branch
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
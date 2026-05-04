namespace WpfApp2
{
    public static class Session
    {
        private static readonly object _lock = new();
        private static int _userId;
        private static string _username = string.Empty;
        private static string _displayName = string.Empty;
        private static string _role = string.Empty;

        public static int UserId
        {
            get { lock (_lock) return _userId; }
            set { lock (_lock) _userId = value; }
        }

        public static string Username
        {
            get { lock (_lock) return _username; }
            set { lock (_lock) _username = value; }
        }

        public static string DisplayName
        {
            get { lock (_lock) return _displayName; }
            set { lock (_lock) _displayName = value; }
        }

        public static string Role
        {
            get { lock (_lock) return _role; }
            set { lock (_lock) _role = value; }
        }

        public static bool IsMaster => Role == "master";

        public static void Clear()
        {
            lock (_lock)
            {
                _userId = 0;
                _username = string.Empty;
                _displayName = string.Empty;
                _role = string.Empty;
            }
        }
    }
}

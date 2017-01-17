namespace eTapeViewer
{
    internal static class RoamingSettings
    {
        public static int GetInt(Settings s)
        {
            var r = Windows.Storage.ApplicationData.Current.RoamingSettings.Values[s.ToString()];

            if (r is int)
                return (int)r;

            return 0;
        }

        public static void IncrementInt(Settings s)
        {
            SetInt(s,GetInt(s) + 1);
        }

        internal static void SetInt(Settings s, int i)
        {
            Windows.Storage.ApplicationData.Current.RoamingSettings.Values[s.ToString()] = i;
        }

        public static void DecrementInt(Settings s)
        {
            SetInt(s, GetInt(s) - 1);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Controls;

namespace WSLBox
{
    [DataContract]
    internal sealed class LocalizedString
    {
        [DataMember(Name = "zh-CN")] public string Chinese { get; set; }
        [DataMember(Name = "en-US")] public string English { get; set; }
    }
    internal static class L10n
    {
        private static readonly string systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        private static readonly Dictionary<string, LocalizedString> strings = Load();
        public static string Preference { get; private set; }
        public static string Language { get; private set; }
        static L10n() { Initialize("auto"); }
        private static Dictionary<string, LocalizedString> Load()
        {
            using (Stream source = typeof(L10n).Assembly.GetManifestResourceStream("WSLBox.Strings"))
            {
                if (source == null) throw new InvalidOperationException("Embedded language catalog is missing.");
                var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, LocalizedString>),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                return (Dictionary<string, LocalizedString>)serializer.ReadObject(source);
            }
        }
        public static string Normalize(string value)
        {
            return value == "zh-CN" || value == "zh" ? "zh-CN" : value == "en-US" || value == "en" ? "en-US" : "auto";
        }
        public static void Initialize(string preference)
        {
            Preference = Normalize(preference);
            Language = Preference == "auto"
                ? (systemLanguage == "zh" ? "zh-CN" : "en-US") : Preference;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
        }
        public static string T(string key)
        {
            LocalizedString value;
            if (!strings.TryGetValue(key, out value)) return key;
            return Language == "zh-CN" ? value.Chinese ?? value.English ?? key : value.English ?? value.Chinese ?? key;
        }
        public static string F(string key, params object[] arguments) { return String.Format(CultureInfo.GetCultureInfo(Language), T(key), arguments); }
        public static MenuItem LanguageMenu(Func<string> preference, Action<string> change)
        {
            var root = new MenuItem { Header = "Language / 语言" };
            foreach (var pair in Choices)
            {
                string code = pair.Key;
                var item = new MenuItem { Header = pair.Value, IsCheckable = true };
                item.Click += (s,e) => change(code);
                root.Items.Add(item);
            }
            root.SubmenuOpened += (s,e) => {
                foreach (MenuItem item in root.Items) item.IsChecked = (string)item.Tag == Normalize(preference());
            };
            for (int index = 0; index < Choices.Length; index++) ((MenuItem)root.Items[index]).Tag = Choices[index].Key;
            return root;
        }
        public static readonly KeyValuePair<string,string>[] Choices = {
            new KeyValuePair<string,string>("auto", "System / 跟随系统"),
            new KeyValuePair<string,string>("zh-CN", "简体中文"),
            new KeyValuePair<string,string>("en-US", "English")
        };
    }
}

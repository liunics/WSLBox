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
        public const string LanguageMenuLabel = "Language / 语言";

        public static Action BindLanguageChoices(Func<string> preference, Action<string> change,
            Func<string, Action, Action<bool>> addChoice)
        {
            var checks = new List<KeyValuePair<string, Action<bool>>>();
            foreach (var pair in Choices)
            {
                string code = pair.Key;
                checks.Add(new KeyValuePair<string, Action<bool>>(code, addChoice(pair.Value, () => change(code))));
            }
            return () =>
            {
                string selected = Normalize(preference());
                foreach (var check in checks) check.Value(check.Key == selected);
            };
        }

        public static MenuItem LanguageMenu(Func<string> preference, Action<string> change)
        {
            var root = new MenuItem { Header = LanguageMenuLabel };
            Action refresh = BindLanguageChoices(preference, change, (label, select) =>
            {
                var item = new MenuItem { Header = label, IsCheckable = true };
                item.Click += (s, e) => { select(); item.IsChecked = true; };
                root.Items.Add(item);
                return value => item.IsChecked = value;
            });
            root.SubmenuOpened += (s, e) => refresh();
            return root;
        }
        public static readonly KeyValuePair<string,string>[] Choices = {
            new KeyValuePair<string,string>("auto", "System / 跟随系统"),
            new KeyValuePair<string,string>("zh-CN", "简体中文"),
            new KeyValuePair<string,string>("en-US", "English")
        };
    }
}

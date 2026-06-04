using Domain;
using E7FRSAdvance.Controllers;
using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

namespace E7FRSAdvance.Utility
{

    public static class ExtensionMethod
    {

        public static string ToTrim(this string inputtedString)
        {
            if (inputtedString.Contains("\r") || inputtedString.Contains("\t") || inputtedString.Contains("\n"))
            {
                inputtedString = inputtedString.Replace("\n", "").Replace("\r", "").Replace("\t", "");
            }
            if (!string.IsNullOrEmpty(inputtedString) && !string.IsNullOrWhiteSpace(inputtedString))
            {
                inputtedString = inputtedString.Trim();
            }
            return inputtedString;
        }

        public static string StrToUpper(this string inputtedString)
        {
            if (!string.IsNullOrEmpty(inputtedString) && !string.IsNullOrWhiteSpace(inputtedString))
                inputtedString = inputtedString.Trim().ToUpper();
            return inputtedString;
        }

        public static string StrToLower(this string inputtedString)
        {
            if (!string.IsNullOrEmpty(inputtedString) && !string.IsNullOrWhiteSpace(inputtedString))
                inputtedString = inputtedString.Trim().ToLower();
            return inputtedString;
        }

        public static DateTime TryParseToDate(this string stringDateTime)
        {
            bool hasDate = false;
            DateTime dateTime = new DateTime();
            if (!string.IsNullOrEmpty(stringDateTime) && !string.IsNullOrWhiteSpace(stringDateTime))
            {
                string[] inputText = stringDateTime.Split(' ');
                foreach (string text in inputText)
                {
                    try
                    {
                        dateTime = DateTime.Parse(text);
                        hasDate = true;
                        break;
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            if (hasDate)
                return dateTime;
            else
                return DateTime.MinValue;
        }

        public static bool IsNotNullOrEmpty(this string inputtedString)
        {
            bool isNotNullOrEmpty = false;
            if (!string.IsNullOrEmpty(inputtedString) && !string.IsNullOrWhiteSpace(inputtedString))
            {
                isNotNullOrEmpty = true;
            }
            return isNotNullOrEmpty;
        }

        public static bool IsNullOrEmpty(this string inputtedString)
        {
            bool isNullOrEmpty = false;
            if (string.IsNullOrEmpty(inputtedString) || string.IsNullOrWhiteSpace(inputtedString))
            {
                isNullOrEmpty = true;
            }
            return isNullOrEmpty;
        }

        public static string RemoveSpecialCharactersWithSpace(string inputtedString)
        {
            if (inputtedString.IsNotNullOrEmpty())
            {
                inputtedString = Regex.Replace(inputtedString, "[^a-zA-Z0-9 ]+", "", RegexOptions.Compiled);

                inputtedString = inputtedString.Replace(" ", "");
            }
            return inputtedString;
        }

        public static string RemoveSpecialChar(this string inputedString)
        {
            string[] replaceables = new[] { "+", "&&", "||", "!", "(", ")", "{", "}", "[", "]", "^", "~", "*", "?", ":", "\\", "\"", "-" };
            if (inputedString.IsNullOrEmpty())
            {
                return inputedString;
            }
            else
            {
                foreach (var symbol in replaceables)
                {
                    inputedString = inputedString.Replace(symbol, string.Empty);
                    inputedString = inputedString.Trim();
                }
                return inputedString;
            }
        }

        public static string RemoveComma(this string inputtedString)
        {
            if (!string.IsNullOrEmpty(inputtedString) && !string.IsNullOrWhiteSpace(inputtedString))
            {
                inputtedString = inputtedString.Replace(",", "");
            }
            return inputtedString;
        }

        public static List<T> ConvertToList<T>(this DataTable dt)
        {
            var columnNames = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName.ToLower()).ToList();
            var properties = typeof(T).GetProperties();
            return dt.AsEnumerable().Select(row =>
            {
                var objT = Activator.CreateInstance<T>();
                foreach (var pro in properties)
                {
                    if (columnNames.Contains(pro.Name.ToLower()))
                    {
                        try
                        {
                            pro.SetValue(objT, row[pro.Name]);
                        }
                        catch (Exception ex)
                        {
                        }
                    }
                }
                return objT;
            }).ToList();
        }

        public static List<string> GetEnumDisplayNames(Enum enm)
        {
            var type = enm.GetType();
            var displayNames = new List<string>();
            var names = Enum.GetNames(type);
            foreach (var name in names)
            {
                var field = type.GetField(name);
                var objArray = field.GetCustomAttributes(typeof(DisplayAttribute), true);
                if (objArray.Length == 0)
                    displayNames.Add(name);

                foreach (DisplayAttribute fd in objArray)
                    displayNames.Add(fd.Name);
            }
            return displayNames;
        }

        public static string GetDisplayValue(this Enum en)
        {
            string displayName = string.Empty;
            displayName = en.GetType()
                .GetMember(en.ToString())
                .FirstOrDefault()
                .GetCustomAttribute<DisplayAttribute>()?
                .GetName();
            if (displayName.IsNullOrEmpty())
            {
                displayName = en.ToString();
            }
            return displayName;
        }

        public static void Switch<T>(this IList<T> array, int index1, int index2)
        {
            var aux = array[index1];
            array[index1] = array[index2];
            array[index2] = aux;
        }

        public static List<T> ToListof<T>(this DataTable dt)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var columnNames = dt.Columns.Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .ToList();
            var objectProperties = typeof(T).GetProperties(flags);
            var targetList = dt.AsEnumerable().Select(dataRow =>
            {
                var instanceOfT = Activator.CreateInstance<T>();

                foreach (var properties in objectProperties.Where(properties => columnNames.Contains(properties.Name) && dataRow[properties.Name] != DBNull.Value))
                {
                    properties.SetValue(instanceOfT, dataRow[properties.Name], null);
                }
                return instanceOfT;
            }).ToList();

            return targetList;
        }

        public static DateTime ConvertToDateTimeFormat(string value, string format)
        {
            DateTime result;
            try
            {
                bool flag = value.IsNullOrEmpty();
                if (flag)
                {
                    result = DateTime.MinValue;
                }
                else
                {
                    CultureInfo invariantCulture = CultureInfo.InvariantCulture;
                    result = DateTime.ParseExact(value, format, invariantCulture);
                }
            }
            catch (Exception)
            {
                result = DateTime.MinValue;
            }
            return result;
        }

        public static DateTime ParseToDate(this string stringDateTime)
        {
            bool hasDate = false;
            DateTime dateTime = new DateTime();
            if (stringDateTime.IsNotNullOrEmpty())
            {
                string[] inputText = stringDateTime.Split(' ');
                if (inputText != null && inputText.Length > 4)
                {
                    inputText = inputText.Where(x => !string.IsNullOrEmpty(x)).ToArray();
                    var date = $"{inputText[2]} {inputText[1]} {inputText[4]} {inputText[3]}";
                    if (date.IsNotNullOrEmpty())
                    {
                        hasDate = DateTime.TryParse(date, out dateTime);
                    }
                }
            }
            if (hasDate)
                return dateTime;
            else
                return DateTime.Now;
        }

        public static string RemoveChars(this string s, params char[] removeChars)
        {
            Contract.Requires<ArgumentNullException>(s != null);
            Contract.Requires<ArgumentNullException>(removeChars != null);
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (!removeChars.Contains(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string MakeTwoChars(this string input)
        {
            if (string.IsNullOrEmpty(input)) return "00";
            return input.Length == 1 ? input.PadLeft(2, '0') : input;
        }

        public static List<DivisionA> BuildDivision(string zoneCode)
        {
            return BuildZones().Where(x => x.Id == zoneCode)
                .SelectMany(z => z.Divisions)
                .Select(d => new DivisionA { Id = d.Id, Name = $"{d.Name} - {d.Id}", Code = d.Code }) // flatten zones → divisions
                .ToList();
        }

        public static DivisionA BuildDivisionByDivisionCode(string divCode)
        {
            return BuildZones()
                .SelectMany(z => z.Divisions).Where(x => x.Id == divCode)
                .Select(d => new DivisionA { Id = d.Id, Name = $"{d.Name} - {d.Id}", Code = d.Code }) // flatten zones → divisions
                .FirstOrDefault();
        }

        public static List<AssetAttributeA> BuildAssetAttribute(string code)
        {
            return BuildAssetTypes().Where(x => x.Id == code)
                .SelectMany(z => z.AssetAttribute)
                .Select(d => new AssetAttributeA { Id = d.Id, Name = $"{d.Name} - {d.Id}" }) // flatten zones → divisions
                .ToList();
        }

        public static List<ZoneA> BuildZone()
        {
            return BuildZones()
                .Select(d => new ZoneA { Id = d.Id, Name = $"{d.Name} - {d.Id}" }) // flatten zones → divisions
                .ToList();
        }

        public static List<ZoneA> BuildZones() => new List<ZoneA>()
        {
            new ZoneA{ Name="CENTRAL RAILWAY", Code="CR", Id="00", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BHUSAVAL", Code="BSL", Id="00"},
                new DivisionA{ Name="MUMBAI CSTM", Code="CSTM", Id="01"},
                new DivisionA{ Name="NAGPUR", Code="NGP", Id="02"},
                new DivisionA{ Name="PUNE", Code="PUNE", Id="03"},
                new DivisionA{ Name="SOLAPUR", Code="SUR", Id="04"},
            }},
            new ZoneA{ Name="EAST CENTRAL RAILWAY", Code="ECR", Id="01", Divisions=new List<DivisionA>{
                new DivisionA{ Name="DANAPUR", Code="DNR", Id="00"},
                new DivisionA{ Name="DHANBAD", Code="DHN", Id="01"},
                new DivisionA{ Name="PT.DEEN DAYAL UPADHYAYA", Code="DDU", Id="02"},
                new DivisionA{ Name="SAMASTIPUR", Code="SPJ", Id="03"},
                new DivisionA{ Name="SONPUR", Code="SEE", Id="04"},
            }},
            new ZoneA{ Name="EAST COAST RAILWAY", Code="ECoR", Id="02", Divisions=new List<DivisionA>{
                new DivisionA{ Name="KHURDA ROAD", Code="KUR", Id="00"},
                new DivisionA{ Name="SAMBALPUR", Code="SBP", Id="01"},
                new DivisionA{ Name="WALTAIR", Code="WAT", Id="02"},
            }},
            new ZoneA{ Name="EASTERN RAILWAY", Code="ER", Id="03", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ASANSOL", Code="ASN", Id="00"},
                new DivisionA{ Name="HOWRAH", Code="HWH", Id="01"},
                new DivisionA{ Name="MALDA", Code="MLDT", Id="02"},
                new DivisionA{ Name="SEALDAH", Code="SDAH", Id="03"},
            }},
            new ZoneA{ Name="NORTH CENTRAL RAILWAY", Code="NCR", Id="04", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AGRA", Code="AGRA", Id="00"},
                new DivisionA{ Name="JHANSI", Code="JHS", Id="01"},
                new DivisionA{ Name="PRAYAGRAJ", Code="PYRJ", Id="02"},
            }},
            new ZoneA{ Name="NORTH EASTERN RAILWAY", Code="NER", Id="05", Divisions=new List<DivisionA>{
                new DivisionA{ Name="IZZATNAGAR", Code="IZN", Id="00"},
                new DivisionA{ Name="LUCKNOW", Code="LJN", Id="01"},
                new DivisionA{ Name="VARANASI", Code="BSB", Id="02"},
            }},
            new ZoneA{ Name="NORTH FRONTIER RAILWAY", Code="NFR", Id="06", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ALIPURDUAR", Code="APD", Id="00"},
                new DivisionA{ Name="KATIHAR", Code="KIR", Id="01"},
                new DivisionA{ Name="LUMDING", Code="LMG", Id="02"},
                new DivisionA{ Name="RANGIYA", Code="RNY", Id="03"},
                new DivisionA{ Name="TINSUKIA", Code="TSK", Id="04"},
            }},
            new ZoneA{ Name="NORTHERN RAILWAY", Code="NR", Id="07", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AMBALA", Code="UMB", Id="00"},
                new DivisionA{ Name="DELHI", Code="DLI", Id="01"},
                new DivisionA{ Name="FEROZPUR", Code="FZP", Id="02"},
                new DivisionA{ Name="LUCKNOW", Code="LKO", Id="03"},
                new DivisionA{ Name="MORADABAD", Code="MB", Id="04"},
            }},
            new ZoneA{ Name="NORTH WESTERN RAILWAY", Code="NWR", Id="08", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AJMER", Code="AII", Id="00"},
                new DivisionA{ Name="BIKANER", Code="BKN", Id="01"},
                new DivisionA{ Name="JAIPUR", Code="JP", Id="02"},
                new DivisionA{ Name="JODHPUR", Code="JU", Id="03"},
            }},
            new ZoneA{ Name="SOUTH CENTRAL RAILWAY", Code="SCR", Id="09", Divisions=new List<DivisionA>{
                new DivisionA{ Name="GUNTAKAL", Code="GTL", Id="00"},
                new DivisionA{ Name="GUNTUR", Code="JNT", Id="01"},
                new DivisionA{ Name="HYDERABAD", Code="HYB", Id="02"},
                new DivisionA{ Name="NANDED", Code="NED", Id="03"},
                new DivisionA{ Name="SECUNDERABAD", Code="SC", Id="04"},
                new DivisionA{ Name="VIJAYAWADA", Code="BZA", Id="05"},
            }},
            new ZoneA{ Name="SOUTH EAST CENTRAL RAILWAY", Code="SECR", Id="0A", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BILASPUR", Code="BSP", Id="00"},
                new DivisionA{ Name="NAGPUR", Code="NGP", Id="01"},
                new DivisionA{ Name="RAIPUR", Code="R", Id="02"},
            }},
            new ZoneA{ Name="SOUTH EASTERN RAILWAY", Code="SER", Id="0B", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ADRA", Code="ADRA", Id="00"},
                new DivisionA{ Name="CHAKARDHARPUR", Code="CKP", Id="01"},
                new DivisionA{ Name="KHARAGPUR", Code="KGP", Id="02"},
                new DivisionA{ Name="RANCHI", Code="RNC", Id="03"},
            }},
            new ZoneA{ Name="SOUTHERN RAILWAY", Code="SR", Id="0C", Divisions=new List<DivisionA>{
                new DivisionA{ Name="CHENNAI", Code="MAS", Id="00"},
                new DivisionA{ Name="MADURAI", Code="MDU", Id="01"},
                new DivisionA{ Name="PALAKKAD", Code="PGT", Id="02"},
                new DivisionA{ Name="SALEM", Code="SA", Id="03"},
                new DivisionA{ Name="THIRUVANANTHAPURAM", Code="TVC", Id="04"},
                new DivisionA{ Name="TIRUCHCHIRAPPALLI", Code="TPJ", Id="05"},
            }},
            new ZoneA{ Name="SOUTH WESTERN RAILWAY", Code="SWR", Id="0D", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BENGALURU", Code="SBC", Id="00"},
                new DivisionA{ Name="HUBBALLI", Code="UBL", Id="01"},
                new DivisionA{ Name="MYSURU", Code="MYS", Id="02"},
            }},
            new ZoneA{ Name="WEST CENTRAL RAILWAY", Code="WCR", Id="0E", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BHOPAL", Code="BPL", Id="00"},
                new DivisionA{ Name="JABALPUR", Code="JBP", Id="01"},
                new DivisionA{ Name="KOTA", Code="KOTA", Id="02"},
            }},
            new ZoneA{ Name="WESTERN RAILWAY", Code="WR", Id="0F", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AHEMDABAD", Code="ADI", Id="00"},
                new DivisionA{ Name="BHAVNAGAR", Code="BVC", Id="01"},
                new DivisionA{ Name="MUMBAI CENTRAL", Code="BCT", Id="02"},
                new DivisionA{ Name="RAJKOT", Code="RJT", Id="03"},
                new DivisionA{ Name="RATLAM", Code="RTM", Id="04"},
                new DivisionA{ Name="VADODARA", Code="BRC", Id="05"},
            }},
        };

        public static List<AssetAttributeA> BuildAssetType()
        {
            return BuildAssetTypes()
                .Select(d => new AssetAttributeA { Id = d.Id, Name = $"{d.Name} - {d.Id}" }) // flatten zones → divisions
                .ToList();
        }

        public static List<AssetTypeA> BuildAssetTypes() => new List<AssetTypeA>()
        {
            new AssetTypeA{ Id="00", Code="EOP", Name="Point Machine", AssetAttribute = new List<AssetAttributeA>
            {
                new AssetAttributeA { Name = "24 VDC at RR from Loc Normal",   Code = "Vrr_Norm", Id = "00" },
                new AssetAttributeA { Name = "24 VDC at RR from Loc Reverse",  Code = "Vrr_Rev",  Id = "01" },
                new AssetAttributeA { Name = "Digital status of NWKR",          Code = "NWR",     Id = "10" },
                new AssetAttributeA { Name = "Digital status of RWKR",          Code = "RWR",     Id = "11" },
                new AssetAttributeA { Name = "Digital status of NWCR",         Code = "NWCR",    Id = "12" },
                new AssetAttributeA { Name = "Digital status of RWCR",         Code = "RWCR",    Id = "13" },

                new AssetAttributeA { Name = "110 DC at Loc box for Normal",   Code = "Vpt110DC_N", Id = "20" },
                new AssetAttributeA { Name = "110 DC at Loc box for Reverse",  Code = "Vpt110DC_R", Id = "21" },
                new AssetAttributeA { Name = "Point Machine Current Normal",   Code = "IPt_N",      Id = "30" },
                new AssetAttributeA { Name = "Point Machine Current Reverse",  Code = "IPt_R",      Id = "31" },
                new AssetAttributeA { Name = "24 V DC going to Relay Room after detection at LOC for Normal", Code =             "V24DC_LOC_N", Id = "40" },
                new AssetAttributeA { Name = "24 V DC going to Relay Room after detection at LOC for Reverse", Code =             "V24DC_LOC_R", Id = "41" },
                new AssetAttributeA { Name = "Vibration",                      Code = "Vib",        Id = "50" },
                new AssetAttributeA { Name = "Normal Operation time",          Code = "Tpt_N",      Id = "60" },
                new AssetAttributeA { Name = "Reverse Operation time",         Code = "Tpt_R",      Id = "61" }
            }},
            new AssetTypeA{ Id="10", Code="LED", Name="Main Signal", AssetAttribute = new List<AssetAttributeA>
            {
                new AssetAttributeA { Name = "Digital status of HECR",   Code = "HECR",   Id = "00" },
                new AssetAttributeA { Name = "Digital status of RECR",   Code = "RECR",   Id = "01" },
                new AssetAttributeA { Name = "Digital status of DECR",   Code = "DECR",   Id = "02" },
                new AssetAttributeA { Name = "Digital status of HHECR",  Code = "HHECR",  Id = "03" },
                new AssetAttributeA { Name = "Digital status of DR",     Code = "DR",     Id = "10" },
                new AssetAttributeA { Name = "Digital status of HR",     Code = "HR",     Id = "11" },
                new AssetAttributeA { Name = "Digital status of HHR",    Code = "HHR",    Id = "12" },

                new AssetAttributeA { Name = "DPR Voltage",              Code = "VSig_DPR",  Id = "20" },
                new AssetAttributeA { Name = "HPR Voltage",              Code = "VSig_HPR",  Id = "21" },
                new AssetAttributeA { Name = "HHPR Voltage",             Code = "VSig_HHPR", Id = "22" },
                new AssetAttributeA { Name = "Green Aspect Voltage",     Code = "VSig_DG",   Id = "30" },
                new AssetAttributeA { Name = "Yellow Aspect Voltage",    Code = "VSig_HG",   Id = "31" },
                new AssetAttributeA { Name = "Double Yellow Aspect Voltage", Code = "VSig_HHG", Id = "32" },
                new AssetAttributeA { Name = "Red Aspect Voltage",       Code = "VSig_RG",   Id = "33" },
                new AssetAttributeA { Name = "Green Aspect current",     Code = "ISig_DG",   Id = "40" },
                new AssetAttributeA { Name = "Yellow Aspect current",    Code = "ISig_HG",   Id = "41" },
                new AssetAttributeA { Name = "Double Yellow Aspect current", Code = "ISig_HHG", Id = "42" },
                new AssetAttributeA { Name = "Red Aspect current",       Code = "ISig_RG",   Id = "43" }
            }},
            new AssetTypeA{ Id="11", Code="LES", Name="Shunt Signal", AssetAttribute = new List<AssetAttributeA>
            {
                new AssetAttributeA { Name = "Digital status of Sh-ECROFF", Code = "Sh-ECROFF", Id = "00" },
                new AssetAttributeA { Name = "Digital status of Sh-ECRON",  Code = "Sh-ECRON",  Id = "01" },
                new AssetAttributeA { Name = "Digital status of Sh-HR",     Code = "Sh-HR",     Id = "10" },
                new AssetAttributeA { Name = "Shunt HPR Voltage",           Code = "VSigSh_HPR", Id = "20" },
                new AssetAttributeA { Name = "Shunt ON Aspect Voltage",     Code = "VSigSh_ON",   Id = "30" },
                new AssetAttributeA { Name = "Shunt OFF Aspect Voltage",    Code = "VSigSh_OFF",  Id = "31" },
                new AssetAttributeA { Name = "Shunt ON Aspect current",     Code = "ISigSh_ON",   Id = "40" },
                new AssetAttributeA { Name = "Shunt OFF Aspect current",    Code = "ISigSh_OFF",  Id = "41" },
                new AssetAttributeA { Name = "Shunt PILOT Aspect current",  Code = "ISigSh_PILOT",Id = "42" }
            }},
            new AssetTypeA{ Id="12", Code="LEC", Name="Calling On Signal", AssetAttribute = new List<AssetAttributeA>
            {
                new AssetAttributeA { Name = "Digital status of Co-HECR", Code = "Co-HECR", Id = "00" },
                new AssetAttributeA { Name = "Digital status of Co-HR",   Code = "Co-HR",   Id = "10" },

                new AssetAttributeA { Name = "Co-HPR Voltage",            Code = "VCoSig_HPR", Id = "20" },
                new AssetAttributeA { Name = "Calling ON Aspect Voltage", Code = "VCoSig",      Id = "30" },
                new AssetAttributeA { Name = "Calling ON Aspect current", Code = "ICoSig",      Id = "40" }
            }},
            new AssetTypeA{ Id="13", Code="LER", Name="Route Signal", AssetAttribute = new List<AssetAttributeA>
{
    // Relay Room
    new AssetAttributeA { Name = "Digital status of UCER", Code = "UCER", Id = "00" },
    new AssetAttributeA { Name = "Digital status of UHR",  Code = "UHR",  Id = "10" },

    // Location Box
    new AssetAttributeA { Name = "Route HPR Voltage",      Code = "VRoSig_HPR", Id = "20" },
    new AssetAttributeA { Name = "Route Aspect Voltage",   Code = "VRoSig",     Id = "30" },
    new AssetAttributeA { Name = "Route Aspect current",   Code = "IRoSig",     Id = "40" }
}},
            new AssetTypeA{ Id="20", Code="DCT", Name="DC Track Circuit", AssetAttribute = new List<AssetAttributeA>
{
    new AssetAttributeA { Name = "24 VDC TPR UP",               Code = "Vtpr_Up",      Id = "00" },
    new AssetAttributeA { Name = "Digital status of TPR Relay", Code = "TPR",          Id = "10" },
    new AssetAttributeA { Name = "Track Feed Charger I/P Voltage",  Code = "VtcCh_Up",  Id = "20" },
    new AssetAttributeA { Name = "Track Feed Charger O/P Voltage",  Code = "ItcCh_Up",  Id = "21" },
    new AssetAttributeA { Name = "Track Feed Charger O/P Current",  Code = "VtcCh_Dp",  Id = "22" },
    new AssetAttributeA { Name = "Voltage drop at feed end choke",  Code = "ItcCh_Dp",  Id = "23" },
    new AssetAttributeA { Name = "Track Feed end voltage (going to Rails)", Code = "VtcFd_Ch",  Id = "24" },
    new AssetAttributeA { Name = "Track Feed Current", Code = "VtcFd", Id = "25" },
    new AssetAttributeA { Name = "Bat Charging Current",             Code = "ItcFd",     Id = "26" },
    new AssetAttributeA { Name = "Voltage at Variable Track Resistance", Code = "VtcVar", Id = "27" },
    new AssetAttributeA { Name = "Feed end choke resistance", Code = "VtcChRes", Id = "28" },
    new AssetAttributeA { Name = "Variable resistance",    Code = "Rtc_Var",   Id = "29" },

    // Relay End
    new AssetAttributeA { Name = "Track Relay Voltage (Coming from Rail)", Code = "VtcRe", Id = "40" },
    new AssetAttributeA { Name = "Track Relay end Current",                Code = "ItcRe", Id = "41" },
    new AssetAttributeA { Name = "Track Relay Voltage QTA2 (1.4 V, 350mA)",     Code = "VtcReStd", Id = "42" },

    // Extended Parameters
    new AssetAttributeA { Name = "Track Relay Voltage QBAT (1.75V, xxxxxx)",  Code = "VtcReGBAT", Id = "43" },
    new AssetAttributeA { Name = "24 V DC going to TPR after TR pick up contact",  Code = "VtcReGBAT", Id = "44" },
    new AssetAttributeA { Name = "Relay end Choke Voltage",          Code = "V24_TPR",   Id = "50" },
    new AssetAttributeA { Name = "Relay end choke resistance (kOhm)",      Code = "RtcCh",     Id = "51" },
    new AssetAttributeA { Name = "Ballast Sleeper Current",                Code = "I_Ballast", Id = "60" },
    new AssetAttributeA { Name = "Rail Resistance",                        Code = "R_Rail",    Id = "61" }
}},
            new AssetTypeA{ Id="21", Code="SSE", Name="SSAC – Eldyne", AssetAttribute = new List<AssetAttributeA>{

            }},
            new AssetTypeA{ Id="22", Code="SSC", Name="SSAC – CEL", AssetAttribute = new List<AssetAttributeA>{

            }},
            new AssetTypeA{ Id="23", Code="SSG", Name="SSAC – GG Tronics", AssetAttribute = new List<AssetAttributeA>{

            }},
            new AssetTypeA{ Id="24", Code="MSE", Name="MSAC – Eldyne"},
            new AssetTypeA{ Id="25", Code="MSC", Name="MSAC – CEL"},
            new AssetTypeA{ Id="26", Code="MSS", Name="MSAC – Siemens"},
            new AssetTypeA{ Id="27", Code="ACM", Name="MSAC – Siemens ACM200"},
            new AssetTypeA{ Id="28", Code="MSF", Name="MSAC – Frauscher"},
            new AssetTypeA{ Id="29", Code="MSM", Name="MSAC – Medha"},
            new AssetTypeA{ Id="2A", Code="MSG", Name="MSAC – GG Tronics"},
            new AssetTypeA{ Id="2B", Code="MSA", Name="MSAC – Sigma Altpro"},
            new AssetTypeA{ Id="2C", Code="HAG", Name="HA SS DAC – GG Tronics"},
            new AssetTypeA{ Id="2D", Code="AFA", Name="AF Track Circuit – Ansaldo/Alstom"},
            new AssetTypeA{ Id="2E", Code="AFS", Name="AF Track Circuit – Siemens"},
            new AssetTypeA{ Id="2F", Code="AFB", Name="AF Track Circuit – Bombardier"},
            new AssetTypeA{ Id="30", Code="BUA", Name="BPAC with UAC"},
            new AssetTypeA{ Id="31", Code="BUF", Name="BPAC with UFSBI"},
            new AssetTypeA{ Id="32", Code="SGE", Name="SGE Double line Block Instrument"},
            new AssetTypeA{ Id="33", Code="DDT", Name="DIODO Single Line Block Instrument"},
            new AssetTypeA{ Id="34", Code="PBT", Name="Block Instrument – Push Button"},
            new AssetTypeA{ Id="35", Code="NLT", Name="Push Button – Neal's Token"},
            new AssetTypeA{ Id="40", Code="MLC", Name="Mechanical LC Gate"},
            new AssetTypeA{ Id="41", Code="ELC", Name="Electrical LC Gate"},
            new AssetTypeA{ Id="50", Code="IPS", Name="Integrated Power Supply", AssetAttribute = new List< AssetAttributeA>
            {
               new AssetAttributeA { Name = "IPS 110 DC O/P Voltage",              Code = "VIPS 110 DC",            Id = "00" },
                new AssetAttributeA { Name = "IPS 110 AC Sig-1 O/P Voltage",        Code = "VIPS Sig-1 110 AC",      Id = "10" },
                new AssetAttributeA { Name = "IPS 110 AC Sig-2 O/P Voltage",        Code = "VIPS Sig-2 110 AC",      Id = "11" },
                new AssetAttributeA { Name = "IPS 110 AC Sig-3 O/P Voltage",        Code = "VIPS Sig-3 110 AC",      Id = "12" },
                new AssetAttributeA { Name = "IPS 110 AC Sig-4 O/P Voltage",        Code = "VIPS Sig-4 110 AC",      Id = "13" },

                new AssetAttributeA { Name = "IPS 110 AC TR-1 O/P Voltage",         Code = "VIPS TR-1 110 AC",       Id = "20" },
                new AssetAttributeA { Name = "IPS 110 AC TR-2 O/P Voltage",         Code = "VIPS TR-2 110 AC",       Id = "21" },
                new AssetAttributeA { Name = "IPS 110 AC TR-3 O/P Voltage",         Code = "VIPS TR-3 110 AC",       Id = "22" },
                new AssetAttributeA { Name = "IPS 110 AC TR-4 O/P Voltage",         Code = "VIPS TR-4 110 AC",       Id = "23" },

                new AssetAttributeA { Name = "IPS SMR-1 Voltage",                   Code = "VIPS SMR-1 110 DC",      Id = "30" },
                new AssetAttributeA { Name = "IPS SMR-2 Voltage",                   Code = "VIPS SMR-2 110 DC",      Id = "31" },
                new AssetAttributeA { Name = "IPS SMR-3 Voltage",                   Code = "VIPS SMR-3 110 DC",      Id = "32" },
                new AssetAttributeA { Name = "IPS SMR-4 Voltage",                   Code = "VIPS SMR-4 110 DC",      Id = "34" },
                new AssetAttributeA { Name = "IPS SMR-5 Voltage",                   Code = "VIPS SMR-5 110 DC",      Id = "35" },

                new AssetAttributeA { Name = "IPS DC-DC Relay Internal Voltage",                   Code = "VIPS DC R INT",                   Id = "40" },
                new AssetAttributeA { Name = "IPS DC-DC Relay External Voltage",                   Code = "VIPS DC R EXT",                   Id = "48" },
                new AssetAttributeA { Name = "IPS DC-DC AXLE COUNTER Voltage",                     Code = "VIPS DC AXLE C",                  Id = "50" },
                new AssetAttributeA { Name = "IPS DC-DC PANEL INDICATION Voltage",                 Code = "VIPS DC PAN IND",                 Id = "58" },
                new AssetAttributeA { Name = "IPS DC-DC BLOCK LOCAL Voltage",                      Code = "VIPS DC BLOCK LOCAL",             Id = "5C" },
                new AssetAttributeA { Name = "IPS DC-DC HKT MAGNETO Voltage",                      Code = "VIPS DC HKT MAG",                 Id = "60" },

                new AssetAttributeA { Name = "IPS DC-DC BLOCK LINE UP Voltage",                   Code = "VIPS DC BLOCK LINE UP",            Id = "62" },
                new AssetAttributeA { Name = "IPS DC-DC BLOCK LINE DN Voltage",                   Code = "VIPS DC BLOCK LINE DN",            Id = "63" },
                new AssetAttributeA { Name = "IPS DC-DC BLOCK TELE UP Voltage",                   Code = "VIPS DC BLOCK TEL UP",             Id = "65" },
                new AssetAttributeA { Name = "IPS DC-DC BLOCK TELE DN Voltage",                   Code = "VIPS DC BLOCK TEL DN",             Id = "66" },

                new AssetAttributeA { Name = "IPS DC-DC DATALOGGER Voltage",              Code = "VIPS DC DATALOG",            Id = "68" },
                new AssetAttributeA { Name = "IPS DC-DC EI Voltage",                      Code = "VIPS DC EI",                 Id = "70" },

                new AssetAttributeA { Name = "IPS Batterry charging Current",                 Code = "IIPS Batt Char 110 DC",                Id = "78" },
                new AssetAttributeA { Name = "Individual battery voltage (optional)",         Code = "VIPS Batt-1 VIPS Batt-2…",             Id = "80-BF" },
                new AssetAttributeA { Name = "SPD – 1 status",                                Code = "SPD1",                                 Id = "C0" }


            }},
            new AssetTypeA{ Id="60", Code="ELD", Name="Earth Leakage Detector"},
            new AssetTypeA{ Id="FC", Code="RRDOOR", Name="Relay Room Door (Non-asset)"},
            new AssetTypeA{ Id="FD", Code="TEMP", Name="Temperature", AssetAttribute = new List<AssetAttributeA>{
                   new AssetAttributeA { Name = "Temperature 1",         Code = "TEMP1",       Id = "00" },
                new AssetAttributeA { Name = "Temperature 2",         Code = "TEMP2",       Id = "01" },
                new AssetAttributeA { Name = "Temperature 3",         Code = "TEMP3",       Id = "02" },
                new AssetAttributeA { Name = "Temperature 4",         Code = "TEMP4",       Id = "03" },
                new AssetAttributeA { Name = "Temperature 5",         Code = "TEMP5",       Id = "04" },
                new AssetAttributeA { Name = "Temperature 6",         Code = "TEMP6",       Id = "05" },
                new AssetAttributeA { Name = "Temperature 7",         Code = "TEMP7",       Id = "06" },
                new AssetAttributeA { Name = "Temperature 8",         Code = "TEMP8",       Id = "07" },
                new AssetAttributeA { Name = "Temperature 9",         Code = "TEMP9",       Id = "08" },
                new AssetAttributeA { Name = "Temperature 10",        Code = "TEMP10",      Id = "09" },
                new AssetAttributeA { Name = "Temperature 11",        Code = "TEMP11",      Id = "0A" },
                new AssetAttributeA { Name = "Temperature 12",        Code = "TEMP12",      Id = "0B" },
                new AssetAttributeA { Name = "Temperature 13",        Code = "TEMP13",      Id = "0C" },
                new AssetAttributeA { Name = "Temperature 14",        Code = "TEMP14",      Id = "0D" },
                new AssetAttributeA { Name = "Temperature 15",        Code = "TEMP15",      Id = "0E" },
                new AssetAttributeA { Name = "Temperature 16",        Code = "TEMP16",      Id = "0F" }
            }},
            new AssetTypeA{ Id="FE", Code="HUMD", Name="Humidity", AssetAttribute = new List<AssetAttributeA>{
                new AssetAttributeA { Name = "Humidity 1",         Code = "HUMD1",       Id = "00" },
                new AssetAttributeA { Name = "Humidity 2",         Code = "HUMD2",       Id = "01" },
                new AssetAttributeA { Name = "Humidity 3",         Code = "HUMD3",       Id = "02" },
                new AssetAttributeA { Name = "Humidity 4",         Code = "HUMD4",       Id = "03" },
                new AssetAttributeA { Name = "Humidity 5",         Code = "HUMD5",       Id = "04" },
                new AssetAttributeA { Name = "Humidity 6",         Code = "HUMD6",       Id = "05" },
                new AssetAttributeA { Name = "Humidity 7",         Code = "HUMD7",       Id = "06" },
                new AssetAttributeA { Name = "Humidity 8",         Code = "HUMD8",       Id = "07" },
                new AssetAttributeA { Name = "Humidity 9",         Code = "HUMD9",       Id = "08" },
                new AssetAttributeA { Name = "Humidity 10",        Code = "HUMD10",      Id = "09" },
                new AssetAttributeA { Name = "Humidity 11",        Code = "HUMD11",      Id = "0A" },
                new AssetAttributeA { Name = "Humidity 12",        Code = "HUMD12",      Id = "0B" },
                new AssetAttributeA { Name = "Humidity 13",        Code = "HUMD13",      Id = "0C" },
                new AssetAttributeA { Name = "Humidity 14",        Code = "HUMD14",      Id = "0D" },
                new AssetAttributeA { Name = "Humidity 15",        Code = "HUMD15",      Id = "0E" },
                new AssetAttributeA { Name = "Humidity 16",        Code = "HUMD16",      Id = "0F" }

            }},
            new AssetTypeA{ Id="FF", Code="OTHERS", Name="Miscellaneous", AssetAttribute = new List<AssetAttributeA> { }},
        };

        //public static DataTable CopyToDataTable<T>(this IEnumerable<T> source)
        //{
        //    return new ObjectShredder<T>().Shred(source, null, null);
        //}

        public static List<AssetAttributeA> BuildParameterRepresentation() => new List<AssetAttributeA>()
{
    new AssetAttributeA{ Id="00", Code="IPS110DC", Name="IPS 110 DC O/P Voltage"},
    new AssetAttributeA{ Id="10", Code="IPS110AC_SIG1", Name="IPS 110 AC Sig-1 O/P Voltage"},
    new AssetAttributeA{ Id="11", Code="IPS110AC_SIG2", Name="IPS 110 AC Sig-2 O/P Voltage"},
    new AssetAttributeA{ Id="12", Code="IPS110AC_SIG3", Name="IPS 110 AC Sig-3 O/P Voltage"},
    new AssetAttributeA{ Id="13", Code="IPS110AC_SIG4", Name="IPS 110 AC Sig-4 O/P Voltage"},
    new AssetAttributeA{ Id="20", Code="IPS110AC_TR1", Name="IPS 110 AC TR-1 O/P Voltage"},
    new AssetAttributeA{ Id="21", Code="IPS110AC_TR2", Name="IPS 110 AC TR-2 O/P Voltage"},
    new AssetAttributeA{ Id="22", Code="IPS110AC_TR3", Name="IPS 110 AC TR-3 O/P Voltage"},
    new AssetAttributeA{ Id="23", Code="IPS110AC_TR4", Name="IPS 110 AC TR-4 O/P Voltage"},
    new AssetAttributeA{ Id="30", Code="IPSSMR1", Name="IPS SMR-1 Voltage"},
    new AssetAttributeA{ Id="31", Code="IPSSMR2", Name="IPS SMR-2 Voltage"},
    new AssetAttributeA{ Id="32", Code="IPSSMR3", Name="IPS SMR-3 Voltage"},
    new AssetAttributeA{ Id="34", Code="IPSSMR4", Name="IPS SMR-4 Voltage"},
    new AssetAttributeA{ Id="35", Code="IPSSMR5", Name="IPS SMR-5 Voltage"},
};


        public static List<GatwayType> BuildGatwayType() => new List<GatwayType>()
        {
            new GatwayType{ Id="00",  Name="Gatway1"},
            new GatwayType{ Id="01",  Name="Gatway2"},
            new GatwayType{ Id="02",  Name="Gatway3"},
            new GatwayType{ Id="03",  Name="Gatway4"},
            new GatwayType{ Id="04", Name="Gatway5"}
        };

        public static List<ParamType> BuildParamTypes() => new List<ParamType>()
        {
            new ParamType{ Id="00", Name="Current DC (A)" },
            new ParamType{ Id="01", Name="Current DC (mA)" },
            new ParamType{ Id="10", Name="Current AC (A)" },
            new ParamType{ Id="11", Name="Current AC (mA)" },
            new ParamType{ Id="20", Name="Voltage DC (V)" },
            new ParamType{ Id="21", Name="Voltage DC (mV)" },
            new ParamType{ Id="30", Name="Voltage AC (V)" },
            new ParamType{ Id="31", Name="Voltage AC (mV)" },
            new ParamType{ Id="40", Name="Digital (1=UP,0=DN)" },
            new ParamType{ Id="50", Name="Temperature (°C)" },
            new ParamType{ Id="51", Name="Humidity (%)" },
            new ParamType{ Id="60", Name="Vibration" },
            new ParamType{ Id="70", Name="Frequency (kHz)" },
            new ParamType{ Id="71", Name="Frequency (Hz)" },
            new ParamType{ Id="80", Name="Resistance (Ω)" },
            new ParamType{ Id="81", Name="Resistance (kΩ)" },
            new ParamType{ Id="90", Name="Time (s)" },
            new ParamType{ Id="91", Name="Time (ms)" },
        };

        public static Dictionary<string, List<ParamRepHelper>> BuildParamRepHelpers() => new Dictionary<string, List<ParamRepHelper>>()
        {
            ["EOP"] = new List<ParamRepHelper>()
            {
                new ParamRepHelper{ Id="10", Code="NWKR", Name="Digital status of NWKR"},
                new ParamRepHelper{ Id="11", Code="RWKR", Name="Digital status of RWKR"},
                new ParamRepHelper{ Id="12", Code="NWCR", Name="Digital status of NWCR"},
                new ParamRepHelper{ Id="13", Code="RWCR", Name="Digital status of RWCR"},
                new ParamRepHelper{ Id="30", Code="IPT N", Name="Point Machine Current Normal"},
                new ParamRepHelper{ Id="31", Code="IPT R", Name="Point Machine Current Reverse"},
                new ParamRepHelper{ Id="60", Code="TPT N", Name="Normal Operation time (derived)"},
                new ParamRepHelper{ Id="61", Code="TPT R", Name="Reverse Operation time (derived)"},
            },
            ["DCT"] = new List<ParamRepHelper>()
            {
                new ParamRepHelper{ Id="10", Code="TPR", Name="Digital status of TPR Relay"},
                new ParamRepHelper{ Id="21", Code="VTC TFC O/P", Name="TFC Output Voltage (AC)"},
                new ParamRepHelper{ Id="22", Code="ITC TFC O/P", Name="TFC Output Current"},
                new ParamRepHelper{ Id="24", Code="VTC FEED END", Name="Feed End Voltage"},
                new ParamRepHelper{ Id="25", Code="ITC FEED END", Name="Feed End Current"},
                new ParamRepHelper{ Id="41", Code="ITC RELAY END", Name="Relay End Current"},
            },
            ["LED"] = new List<ParamRepHelper>()
            {
                new ParamRepHelper{ Id="00", Code="HECR", Name="Digital status of HECR"},
                new ParamRepHelper{ Id="11", Code="HR", Name="Digital status of HR"},
                new ParamRepHelper{ Id="30", Code="VSig DG", Name="Green Aspect Voltage (AC)"},
                new ParamRepHelper{ Id="40", Code="ISig DG", Name="Green Aspect Current"},
            },
        };


        public static object GetSystemUsage()
        {
            var cpu = CpuMetrics.GetCpuUsage();
            var ram = MemoryMetrics.GetRamUsage();
            var disk = DiskMetrics.GetDiskUsage();

            return new
            {
                CpuUsagePercent = Math.Round(cpu, 2),
                Ram = new
                {
                    UsedGB = Math.Round(ram.UsedGB, 2),
                    TotalGB = Math.Round(ram.TotalGB, 2),
                    UsagePercent = Math.Round(ram.UsagePercent, 2)
                },
                Disk = disk,
                Timestamp = DateTime.UtcNow
            };
        }

        public static IEnumerable<T[]> BatchItems<T>(IEnumerable<T> source, int batchSize)
        {
            var collection = new List<T>(batchSize);
            foreach (var item in source)
            {
                collection.Add(item);
                if (collection.Count == batchSize)
                {
                    yield return collection.ToArray();
                    collection.Clear();
                }
            }

            if (collection.Count > 0)
            {
                yield return collection.ToArray();
            }
        }

        public static decimal CalculateAvailability(List<Domain.Dto.ModemHistory> records)
        {
            if (records == null || records.Count == 0)
                return 0m;

            // Sort by timestamp
            List<Domain.Dto.ModemHistory> sorted = records.OrderBy(r => r.TimeStamp).ToList();

            if (sorted.Count == 1)
                return 100m;

            DateTime firstTime = sorted[0].TimeStamp;
            DateTime lastTime = sorted[sorted.Count - 1].TimeStamp;

            double totalMinutes = (lastTime - firstTime).TotalMinutes;
            if (totalMinutes <= 0)
                return 100m;

            // Sum up available gaps (gap <= 2 min = available)
            double availableMinutes = 0;

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                double gapMinutes = (sorted[i + 1].TimeStamp - sorted[i].TimeStamp).TotalMinutes;

                if (gapMinutes <= 2.0)
                {
                    availableMinutes += gapMinutes;
                }
                // else: gap > 2 min = down period, not counted
            }

            decimal availability = (decimal)(availableMinutes / totalMinutes) * 100m;
            return Math.Round(availability, 2);
        }

    }

    public static class CpuMetrics
    {
        private static readonly PerformanceCounter CpuCounter =
            new PerformanceCounter("Processor", "% Processor Time", "_Total");

        public static float GetCpuUsage()
        {
            CpuCounter.NextValue();     // First call returns 0
            Thread.Sleep(1000);         // Mandatory delay
            return CpuCounter.NextValue();
        }
    }

    public static class MemoryMetrics
    {
        public static (double UsedGB, double TotalGB, double UsagePercent) GetRamUsage()
        {
            var computerInfo = new ComputerInfo();

            double total = computerInfo.TotalPhysicalMemory / 1024d / 1024d / 1024d;
            double available = computerInfo.AvailablePhysicalMemory / 1024d / 1024d / 1024d;
            double used = total - available;
            double percent = (used / total) * 100;

            return (used, total, percent);
        }
    }


    public static class DiskMetrics
    {
        public static List<object> GetDiskUsage()
        {
            var disks = new List<object>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                double total = drive.TotalSize / 1024d / 1024d / 1024d;
                double free = drive.TotalFreeSpace / 1024d / 1024d / 1024d;
                double used = total - free;
                double percent = (used / total) * 100;

                disks.Add(new
                {
                    Drive = drive.Name,
                    UsedGB = Math.Round(used, 2),
                    TotalGB = Math.Round(total, 2),
                    UsagePercent = Math.Round(percent, 2)
                });
            }

            return disks;
        }
    }
}
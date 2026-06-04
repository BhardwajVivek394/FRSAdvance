using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Utility
{
    public static class EnumHelper
    {
        public static T GetEnumValue<T>(string str) where T : struct, IConvertible
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new Exception("T must be an Enumeration type.");
            }
            T val;
            return Enum.TryParse<T>(str, true, out val) ? val : default(T);
        }

        public static T GetEnumString<T>(int intValue) where T : struct, IConvertible
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new Exception("T must be an Enumeration type.");
            }

            return (T)Enum.ToObject(enumType, intValue);
        }

        public static string GetDescription<T>(int intValue) where T : struct, IConvertible
        {
            var description = string.Empty;
            try
            {
                Type enumType = typeof(T);
                if (!enumType.IsEnum)
                {
                    throw new Exception("T must be an Enumeration type.");
                }

                var enumValue = Enum.ToObject(enumType, intValue);
                if (enumValue != null)
                {
                    var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());

                    if (fieldInfo != null)
                    {
                        var attrs = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), true);
                        if (attrs != null && attrs.Length > 0)
                        {
                            description = ((DescriptionAttribute)attrs[0]).Description;
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            return description;
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

        public static string GetDisplayName(Enum enumValue)
        {
            return enumValue.GetType()
                            .GetMember(enumValue.ToString())
                            .First()
                            .GetCustomAttribute<DisplayAttribute>()
                            .GetName();
        }


        public static string GetEnumString(ThickWaveType statusType)
        {
            switch (statusType)
            {
                case ThickWaveType.A:
                    return nameof(ThickWaveType.A);
                case ThickWaveType.B:
                    return nameof(ThickWaveType.B);
                case ThickWaveType.Both:
                    return nameof(ThickWaveType.Both);
                default:
                    throw new ArgumentOutOfRangeException(nameof(statusType), statusType, null);
            }
        }

        public static string GetEnumString(ClasificationTypes statusType)
        {
            switch (statusType)
            {
                case ClasificationTypes.Image:
                    return nameof(ClasificationTypes.Image);
                case ClasificationTypes.Array:
                    return nameof(ClasificationTypes.Array);
                default:
                    throw new ArgumentOutOfRangeException(nameof(statusType), statusType, null);
            }
        }

        public static string GetDisplayNameById<TEnum>(int id) where TEnum : Enum
        {
            var enumValue = (TEnum)Enum.ToObject(typeof(TEnum), id);

            var memberInfo = typeof(TEnum).GetMember(enumValue.ToString()).FirstOrDefault();

            var displayAttr = memberInfo?.GetCustomAttribute<DisplayAttribute>();

            return displayAttr?.Name ?? enumValue.ToString();
        }

        public static string GetDisplayName<TEnum>(int value) where TEnum : Enum
        {
            var enumValue = (TEnum)Enum.ToObject(typeof(TEnum), value);
            var member = typeof(TEnum).GetMember(enumValue.ToString()).FirstOrDefault();
            var displayAttr = member?.GetCustomAttribute<DisplayAttribute>();
            return displayAttr?.Name ?? enumValue.ToString();
        }

        public static IEnumerable<SelectListItem> GetEnumSelectList<TEnum>() where TEnum : Enum
        {
            var type = typeof(TEnum);
            return Enum.GetValues(type).Cast<TEnum>().Select(e =>
            {
                var memberInfo = type.GetMember(e.ToString()).First();
                var displayAttr = memberInfo.GetCustomAttribute<DisplayAttribute>();
                var displayName = displayAttr?.Name ?? e.ToString();

                return new SelectListItem
                {
                    Text = displayName,
                    Value = Convert.ToInt32(e).ToString()
                };
            });
        }
    }
}
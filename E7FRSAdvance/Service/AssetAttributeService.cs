using Domain;
using Domain.Dto;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Controllers;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace E7FRSAdvance.Service
{
    public class AssetAttributeService : IAssetAttributeService
    {
        public List<AssetAttribute> GetAll()
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/GetAll").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public List<AssetAttribute> GetAssetAttributesBy(int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/AssetTypeId/{assetTypeId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public List<AssetAttribute> GetByIsDerived(int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/GetByIsDerived/AssetTypeId/{assetTypeId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public List<AssetAttribute> GetAllAssetAttributeBy(int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/GetAllAssetAttributeBy/AssetTypeId/{assetTypeId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public AssetAttribute Get(int id)
        {
            AssetAttribute mAssetAttribute = new AssetAttribute();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributesById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttribute = JsonConvert.DeserializeObject<AssetAttribute>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mAssetAttribute;
        }

        public List<AssetAttribute> GetAssetAttributes(int siteId, int assetId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/SiteId/{siteId}/AssetId/{assetId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public void PrepareGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals)
        {
            if (allAssetAttributes != null && allAssetAttributes.Count > 0 && assetAttributes != null && assetAttributes.Count > 0 && vals.IsNotNullOrEmpty())
            {
                var array = vals.Split('~');
                int i = 3;
                decimal ifma = 0;
                decimal irma = 0;
                foreach (var allAssetAttribute in allAssetAttributes)
                {
                    if (i != 9 || i != 10)
                    {
                        if (allAssetAttribute.Title == "If mA")
                        {
                            if (!string.IsNullOrEmpty(array[i]))
                            {
                                ifma = Convert.ToDecimal(array[i]);
                            }
                            else
                            {
                                ifma = 0;
                            }

                        }
                        if (allAssetAttribute.Title == "Ir mA")
                        {
                            if (!string.IsNullOrEmpty(array[i]))
                            {
                                irma = Convert.ToDecimal(array[i]);
                            }
                            else
                            {
                                irma = 0;
                            }
                        }
                        if (allAssetAttribute.Title == "Leakage")
                        {
                            var assetAttribute = assetAttributes.Where(x => x.Title.Trim() == allAssetAttribute.Title.Trim()).FirstOrDefault();
                            if (assetAttribute != null)
                                assetAttribute.Data.Add(Convert.ToString(ifma - irma));
                            else
                                assetAttribute.Data.Add("0");
                        }
                        else
                        {
                            var assetAttribute = assetAttributes.Where(x => x.Id == allAssetAttribute.Id).FirstOrDefault();
                            if (assetAttribute != null && assetAttribute.Id > 0)
                            {
                                if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                                    assetAttribute.Data.Add(Convert.ToString(array[i]));
                                else
                                    assetAttribute.Data.Add("0");
                            }
                        }
                    }

                    i++;
                }
            }

        }

        public void PrepareWithDataArrayGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals)
        {
            if (allAssetAttributes != null && allAssetAttributes.Count > 0 && assetAttributes != null && assetAttributes.Count > 0 && vals.IsNotNullOrEmpty())
            {
                var array = vals.Split('~');
                int i = 3;
                decimal ifma = 0;
                decimal irma = 0;
                foreach (var allAssetAttribute in allAssetAttributes)
                {
                    if (i != 9 || i != 10)
                    {
                        if (allAssetAttribute.Title == "If mA")
                        {
                            if (!string.IsNullOrEmpty(array[i]))
                            {
                                ifma = Convert.ToDecimal(array[i]);
                            }
                            else
                            {
                                ifma = 0;
                            }

                        }
                        if (allAssetAttribute.Title == "Ir mA")
                        {
                            if (!string.IsNullOrEmpty(array[i]))
                            {
                                irma = Convert.ToDecimal(array[i]);
                            }
                            else
                            {
                                irma = 0;
                            }

                        }
                        if (allAssetAttribute.Title == "Leakage")
                        {
                            var assetAttribute = assetAttributes.Where(x => x.Title.Trim() == allAssetAttribute.Title.Trim()).FirstOrDefault();
                            if (assetAttribute != null)
                                assetAttribute.DataArray.Add(Convert.ToString(ifma - irma));
                        }
                        else
                        {
                            var assetAttribute = assetAttributes.Where(x => x.Id == allAssetAttribute.Id).FirstOrDefault();
                            if (assetAttribute != null && assetAttribute.Id > 0)
                                assetAttribute.DataArray.Add(Convert.ToString(array[i]));
                        }
                    }

                    i++;
                }
            }

        }

        public List<AssetAttribute> GetGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals)
        {
            if (allAssetAttributes != null && allAssetAttributes.Count > 0 && assetAttributes != null && assetAttributes.Count > 0 && vals.IsNotNullOrEmpty())
            {
                var array = vals.Split('~');
                int i = 3;
                decimal ifma = 0;
                decimal irma = 0;
                foreach (var allAssetAttribute in allAssetAttributes)
                {
                    var assetAttribute = assetAttributes.Where(x => x.Id == allAssetAttribute.Id).FirstOrDefault();
                    if (assetAttribute != null && assetAttribute.Id > 0)
                    {
                        assetAttribute.Data = new List<string>();
                        if (i != 9 || i != 10)
                        {
                            if (allAssetAttribute.Title == "If mA")
                            {
                                if (!string.IsNullOrEmpty(array[i]))
                                {
                                    ifma = Convert.ToDecimal(array[i]);
                                }
                                else
                                {
                                    ifma = 0;
                                }

                            }
                            if (allAssetAttribute.Title == "Ir mA")
                            {
                                if (!string.IsNullOrEmpty(array[i]))
                                {
                                    irma = Convert.ToDecimal(array[i]);
                                }
                                else
                                {
                                    irma = 0;
                                }
                            }
                            if (allAssetAttribute.Title == "Leakage")
                            {
                                assetAttribute.Data.Add(Convert.ToString(ifma - irma));
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(array[i]))
                                {
                                    assetAttribute.Data.Add(Convert.ToString(array[i]));
                                }
                                else
                                {
                                    assetAttribute.Data.Add("0");
                                }
                            }
                        }

                        assetAttribute.TimeStamp = Convert.ToDateTime(array[0]);
                    }
                    else
                    {

                    }

                    i++;
                }
            }

            return assetAttributes;
        }

        public void PrepareGraphAttributeWithActualTime(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals, string actualTime, string ChangeTimestamp)
        {
            try
            {
                if (allAssetAttributes != null && allAssetAttributes.Count > 0 && assetAttributes != null && assetAttributes.Count > 0 && vals.IsNotNullOrEmpty() && actualTime.IsNotNullOrEmpty())
                {
                    var array = vals.Split('~');
                    var actualTimeArray = actualTime.Split('~');
                    var changeTimestampArray = ChangeTimestamp.Split('~');
                    int i = 3;
                    decimal ifma = 0;
                    decimal irma = 0;

                    foreach (var allAssetAttribute in allAssetAttributes)
                    {
                        if (i != 9 || i != 10)
                        {
                            if (allAssetAttribute.Title == "If mA")
                            {
                                if (!string.IsNullOrEmpty(array[i]))
                                {
                                    ifma = Convert.ToDecimal(array[i]);
                                }
                                else
                                {
                                    ifma = 0;
                                }

                            }
                            if (allAssetAttribute.Title == "Ir mA")
                            {
                                if (!string.IsNullOrEmpty(array[i]))
                                {
                                    irma = Convert.ToDecimal(array[i]);
                                }
                                else
                                {
                                    irma = 0;
                                }
                            }
                            if (allAssetAttribute.Title == "Leakage")
                            {
                                var assetAttribute = assetAttributes.Where(x => x.Title.Trim() == allAssetAttribute.Title.Trim()).FirstOrDefault();
                                if (assetAttribute != null)
                                {
                                    if (actualTimeArray.Length > i && actualTimeArray[i].IsNotNullOrEmpty())
                                    {
                                        if (changeTimestampArray != null && changeTimestampArray.Length > 0)
                                        {
                                            assetAttribute.Data.Add($"{Convert.ToString(ifma - irma)} ({Convert.ToDateTime(actualTimeArray[i]).ToString("HH:mm:ss")})({Convert.ToDateTime(changeTimestampArray[i]).ToString("HH:mm:ss")})");
                                        }
                                        else
                                        {
                                            assetAttribute.Data.Add($"{Convert.ToString(ifma - irma)} ({Convert.ToDateTime(actualTimeArray[i]).ToString("HH:mm:ss")})");
                                        }

                                    }
                                    else
                                        assetAttribute.Data.Add($"{Convert.ToString(ifma - irma)}");
                                }
                                else
                                    assetAttribute.Data.Add("0");
                            }
                            else
                            {
                                var assetAttribute = assetAttributes.Where(x => x.Id == allAssetAttribute.Id).FirstOrDefault();
                                if (assetAttribute != null && assetAttribute.Id > 0)
                                {
                                    if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                                    {
                                        if (actualTimeArray.Length > i && actualTimeArray[i].IsNotNullOrEmpty())
                                        {
                                            if (changeTimestampArray != null && changeTimestampArray.Length > 0)
                                            {
                                                DateTime.TryParse(changeTimestampArray[i], out DateTime changeTimestamp);
                                                assetAttribute.Data.Add($"{Convert.ToString(array[i])} ({Convert.ToDateTime(actualTimeArray[i]).ToString("HH:mm:ss")})({changeTimestamp.ToString("HH:mm:ss")})");
                                            }
                                            else
                                            {
                                                assetAttribute.Data.Add($"{Convert.ToString(array[i])} ({Convert.ToDateTime(actualTimeArray[i]).ToString("HH:mm:ss")})");
                                            }

                                        }
                                        else
                                            assetAttribute.Data.Add($"{Convert.ToString(array[i])}");

                                    }
                                    else
                                        assetAttribute.Data.Add("0");
                                }
                            }
                        }

                        i++;
                    }
                }

            }
            catch (Exception ex)
            {

            }

        }

        public string PrepareCSV(List<Asset> assets)
        {
            string csv = string.Empty;

            if (assets != null && assets.Count > 0)
            {
                csv += "Site Name,";
                csv += "Date,";
                csv += "Asset Type,";
                foreach (var asset in assets)
                {
                    foreach (var assetAttribute in asset.assetAttributes)
                    {
                        csv += $"{asset.Name} {assetAttribute.Title},";
                    }
                }

                csv += "\r\n";
                var mAssetAttributes = GetAll();

                var timeStamps = assets.SelectMany(x => x.MultipleLog).GroupBy(x => x.TimeStamp).Select(x => x.Key).ToList();

                foreach (var timeStamp in timeStamps)
                {
                    string newRow = string.Empty;
                    bool isUpdateAtType = false;
                    foreach (var asset in assets)
                    {
                        var mAllAssetAttributes = mAssetAttributes.Where(x => x.AssetTypeId == asset.AssetTypeId).ToList();

                        var mMultipleLog = asset.MultipleLog.Where(x => x.TimeStamp == timeStamp).FirstOrDefault();
                        if (mMultipleLog == null)
                        {
                            mMultipleLog = asset.MultipleLog.Where(x => x.TimeStamp.Date == timeStamp.Date && x.TimeStamp.Hour == timeStamp.Hour && x.TimeStamp.Minute == timeStamp.Minute).LastOrDefault();
                        }
                        if (mMultipleLog != null && mMultipleLog.TimeStamp != null && mMultipleLog.TimeStamp != DateTime.MinValue && asset.assetAttributes != null)
                        {
                            if (!isUpdateAtType)
                            {
                                var assetTypeName = asset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();
                                newRow += asset.SiteName + ",";// + row;
                                newRow += timeStamp.ToString() + ",";
                                newRow += assetTypeName + ",";
                                isUpdateAtType = true;
                            }
                            var assetAttributes = GetGraphAttribute(asset.assetAttributes, mAllAssetAttributes, mMultipleLog.CsvData);
                            if (assetAttributes != null && assetAttributes.Count > 0)
                            {
                                foreach (var assetAttribute in assetAttributes)
                                {
                                    if (assetAttribute.Data != null)
                                    {
                                        var data = assetAttribute.Data.FirstOrDefault();
                                        newRow += data + ",";
                                    }
                                    else
                                    {
                                        newRow += "0.0,";
                                    }
                                }
                            }
                        }
                        else
                        {

                        }
                    }

                    if (isUpdateAtType)
                    {
                        //Add the Data rows.
                        csv += newRow.TrimEnd(',');
                        //Add new line.
                        csv += "\r\n";
                    }
                }
            }


            return csv;
        }

        public string PrepareCSVForFastDataProvider(List<Asset> assets)
        {
            if (assets == null || assets.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(1024 * 1024); // pre-allocate ~1MB

            // ── Header ───────────────────────────────────────────────────────────────
            sb.Append("Site Name,Date,Asset Type,");
            foreach (var asset in assets)
                foreach (var attr in asset.assetAttributes)
                    sb.Append(asset.Name).Append(' ').Append(attr.Title).Append(',');
            sb.Append("\r\n");

            // ── Pre-build indexes (O(n) once instead of O(n) per timestamp) ──────────

            // 1. All asset-type attributes keyed by AssetTypeId
            var allAttributes = GetAll()
                .GroupBy(a => a.AssetTypeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 2. For each asset: exact timestamp → log  (O(1) lookup)
            var exactLookup = assets.ToDictionary(
                asset => asset,
                asset => asset.MultipleLog
                              .GroupBy(l => l.TimeStamp)
                              .ToDictionary(g => g.Key, g => g.Last()));

            // 3. For each asset: (date, hour, minute) → log  (fallback, O(1) lookup)
            var fuzzyLookup = assets.ToDictionary(
                asset => asset,
                asset => asset.MultipleLog
                              .GroupBy(l => (l.TimeStamp.Date,
                                             l.TimeStamp.Hour,
                                             l.TimeStamp.Minute))
                              .ToDictionary(g => g.Key, g => g.Last()));

            // 4. Collect all unique timestamps in one pass
            var timestamps = assets
                .SelectMany(a => a.MultipleLog.Select(l => l.TimeStamp))
                .Distinct()   // HashSet under the hood — much faster than GroupBy here
                .OrderBy(t => t)
                .ToList();

            // ── Data rows ────────────────────────────────────────────────────────────
            // Use a thread-local StringBuilder per row if you want Parallel later
            foreach (var ts in timestamps)
            {
                var rowSb = new StringBuilder(512);
                bool wroteHeader = false;

                foreach (var asset in assets)
                {
                    // O(1) exact match
                    if (!exactLookup[asset].TryGetValue(ts, out var log))
                    {
                        // O(1) fuzzy match
                        fuzzyLookup[asset].TryGetValue(
                            (ts.Date, ts.Hour, ts.Minute), out log);
                    }

                    if (log == null || log.TimeStamp == DateTime.MinValue
                                     || asset.assetAttributes == null)
                        continue;

                    if (!wroteHeader)
                    {
                        var typeName = asset.assetAttributes
                                            .Select(x => x.AssetTypeName)
                                            .FirstOrDefault();
                        rowSb.Append(asset.SiteName).Append(',')
                             .Append(ts).Append(',')
                             .Append(typeName).Append(',');
                        wroteHeader = true;
                    }

                    var typeAttrs = allAttributes.TryGetValue(
                        asset.AssetTypeId, out var list) ? list : new List<AssetAttribute>();

                    var attrs = GetGraphAttributeForFastDataProvider(
                        asset.assetAttributes, typeAttrs, log.CsvData);

                    if (attrs == null) continue;
                    foreach (var attr in attrs)
                    {
                        var data = attr.Data?.FirstOrDefault();
                        rowSb.Append(data ?? "0.0").Append(',');
                    }
                }

                if (wroteHeader)
                {
                    // Remove trailing comma, add newline
                    if (rowSb.Length > 0 && rowSb[rowSb.Length - 1] == ',')
                        rowSb.Length--; // faster than TrimEnd
                    rowSb.Append("\r\n");
                    sb.Append(rowSb);
                }
            }

            return sb.ToString();
        }

        public List<AssetAttribute> GetGraphAttributeForFastDataProvider(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals)
        {
            // Guard: return early if any required input is missing
            if (allAssetAttributes == null || allAssetAttributes.Count == 0
                || assetAttributes == null || assetAttributes.Count == 0
                || string.IsNullOrEmpty(vals))
                return assetAttributes;

            // ── Parse CSV string once ────────────────────────────────────────────────
            var array = vals.Split('~');

            // ── Build O(1) lookup: attribute Id → assetAttribute ────────────────────
            // Also clone Data list to avoid mutating shared objects (critical for Parallel use)
            var attrById = assetAttributes
                .Where(a => a.Id > 0)
                .ToDictionary(a => a.Id, a => new AssetAttribute
                {
                    Id = a.Id,
                    Title = a.Title,
                    AssetTypeName = a.AssetTypeName,
                    AssetTypeId = a.AssetTypeId,
                    Data = new List<string>(),
                    TimeStamp = a.TimeStamp
                    // copy any other fields your AssetAttribute has
                });

            // ── Parse timestamp once (index 0) ───────────────────────────────────────
            DateTime parsedTime = DateTime.MinValue;
            if (array.Length > 0 && !string.IsNullOrEmpty(array[0]))
                DateTime.TryParse(array[0], out parsedTime);

            // ── Walk allAssetAttributes in order (index starts at 3) ─────────────────
            int i = 3;
            decimal ifma = 0m;
            decimal irma = 0m;

            foreach (var allAttr in allAssetAttributes)
            {
                // Skip indices 9 and 10 — matches original: if (i != 9 || i != 10)
                // Note: original condition "i != 9 || i != 10" is always true (a number
                // can't equal both 9 and 10). Preserved here as-is. If you meant to
                // SKIP those indices, change to: if (i == 9 || i == 10) { i++; continue; }
                bool indexInBounds = i < array.Length;

                if (!attrById.TryGetValue(allAttr.Id, out var clonedAttr))
                {
                    i++;
                    continue; // attribute not in this asset's list, skip
                }

                // Set timestamp on the cloned attribute
                clonedAttr.TimeStamp = parsedTime;

                // ── Safely read value at current index ───────────────────────────────
                string rawVal = indexInBounds && !string.IsNullOrEmpty(array[i])
                    ? array[i]
                    : null;

                // ── Track If mA and Ir mA for Leakage calculation ────────────────────
                if (allAttr.Title == "If mA")
                    ifma = rawVal != null ? ParseDecimalSafe(rawVal) : 0m;

                if (allAttr.Title == "Ir mA")
                    irma = rawVal != null ? ParseDecimalSafe(rawVal) : 0m;

                // ── Assign Data value ─────────────────────────────────────────────────
                if (allAttr.Title == "Leakage")
                {
                    clonedAttr.Data.Add(Convert.ToString(ifma - irma));
                }
                else
                {
                    clonedAttr.Data.Add(rawVal ?? "0");
                }

                i++;
            }

            // ── Return cloned attributes in the same order as original input ──────────
            // Preserves the original list order, replaces items that were processed
            return assetAttributes
                .Select(a => attrById.TryGetValue(a.Id, out var updated) ? updated : a)
                .ToList();
        }

        // ── Safe decimal parser (avoids exception on bad data) ───────────────────────
        private static decimal ParseDecimalSafe(string val)
        {
            return decimal.TryParse(
                val,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var result) ? result : 0m;
        }

        public string PrepareCSVWithEdgex(List<Asset> assets, RdpmsResponse rdpmsResponse)
        {
            string csv = string.Empty;

            if (assets != null && assets.Count > 0)
            {
                csv += "Site Name,";
                csv += "Date,";
                csv += "Asset Type,";
                foreach (var asset in assets)
                {
                    foreach (var assetAttribute in asset.assetAttributes)
                    {
                        //if (assetAttribute.AliasName.IsNotNullOrEmpty())
                        //{
                        //    csv += $"{asset.Name} {assetAttribute.AliasName},";
                        //}
                        //else
                        //{
                        //    csv += $"{asset.Name} {assetAttribute.Title},";
                        //}

                        csv += $"{asset.Name} {assetAttribute.Title},";
                    }
                }

                csv += "\r\n";


                if (rdpmsResponse != null && rdpmsResponse.Data != null && rdpmsResponse.Data.Count > 0)
                {
                    var startDate = rdpmsResponse.Data.Select(x => x.StartDate).FirstOrDefault();
                    var endDate = rdpmsResponse.Data.Select(x => x.EndDate).FirstOrDefault();
                    var timeStamps = rdpmsResponse.Data.SelectMany(x => x.Values).Where(x => x.Value.DataType == "RDPMS").GroupBy(x => x.Value.Timestamp.TimestampDevice).Select(x => x.Key).OrderBy(x => x).ToList();

                    foreach (var timeStamp in timeStamps.Where(x => x >= startDate))
                    {
                        string newRow = string.Empty;
                        bool isUpdateAtType = false;
                        foreach (var asset in assets)
                        {

                            //if (mMultipleLog == null)
                            //{
                            //    mMultipleLog = asset.MultipleLog.Where(x => x.TimeStamp.Date == timeStamp.Date && x.TimeStamp.Hour == timeStamp.Hour && x.TimeStamp.Minute == timeStamp.Minute).LastOrDefault();
                            //}
                            if (asset.assetAttributes != null)
                            {
                                if (!isUpdateAtType)
                                {
                                    var assetTypeName = asset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();
                                    newRow += asset.SiteName + ",";// + row;
                                    newRow += timeStamp.ToString() + ",";
                                    newRow += assetTypeName + ",";
                                    isUpdateAtType = true;
                                }
                                if (asset.assetAttributes != null && asset.assetAttributes.Count > 0)
                                {
                                    foreach (var assetAttribute in asset.assetAttributes)
                                    {
                                        var mMultipleLog = rdpmsResponse.Data.Where(x => x.AssetId == asset.Id && x.AttributeId == assetAttribute.Id).FirstOrDefault();
                                        if (mMultipleLog != null && mMultipleLog.Values != null && mMultipleLog.Values.Count > 0)
                                        {
                                            var firstValue = mMultipleLog.Values.OrderBy(x => x.Value.Timestamp.TimestampDevice).FirstOrDefault();
                                            if (firstValue.Value != null && assetAttribute.Data.Count == 0)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(firstValue.Value.Value));
                                            }
                                            var changedValue = mMultipleLog.Values
    .Where(x =>
        x.Value.Timestamp.TimestampDevice.Year == timeStamp.Year &&
        x.Value.Timestamp.TimestampDevice.Month == timeStamp.Month &&
        x.Value.Timestamp.TimestampDevice.Day == timeStamp.Day &&
        x.Value.Timestamp.TimestampDevice.Hour == timeStamp.Hour &&
        x.Value.Timestamp.TimestampDevice.Minute == timeStamp.Minute &&
        x.Value.Timestamp.TimestampDevice.Second == timeStamp.Second)
    .FirstOrDefault();
                                            if (changedValue.Value != null)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(changedValue.Value.Value));
                                            }
                                        }

                                        if (assetAttribute.Data != null)
                                        {
                                            var data = assetAttribute.Data.FirstOrDefault();
                                            newRow += data + ",";
                                        }
                                        else
                                        {
                                            newRow += "0.0,";
                                        }
                                    }
                                }
                            }
                            else
                            {

                            }
                        }

                        if (isUpdateAtType)
                        {
                            //Add the Data rows.
                            csv += newRow.TrimEnd(',');
                            //Add new line.
                            csv += "\r\n";
                        }
                    }
                }

            }


            return csv;
        }

        public string PrepareCSVWithEdgexDataLogger(List<Asset> assets, RdpmsResponse rdpmsResponse, List<Domain.AssetInfoDatalogger> dataloggers)
        {
            string csv = string.Empty;

            if (assets != null && assets.Count > 0)
            {
                csv += "Site Name,";
                csv += "Date,";
                csv += "Asset Type,";
                foreach (var asset in assets)
                {
                    foreach (var assetAttribute in asset.assetAttributes)
                    {
                        csv += $"{asset.Name} {assetAttribute.Title},";
                    }
                }

                csv += "\r\n";


                if (rdpmsResponse != null && rdpmsResponse.Data != null && rdpmsResponse.Data.Count > 0)
                {
                    var timeStamps = rdpmsResponse.Data.SelectMany(x => x.Values).Where(x => x.Value.DataType == "RDPMS").GroupBy(x => x.Value.Timestamp.TimestampDevice).Select(x => x.Key).OrderBy(x => x).ToList();

                    foreach (var timeStamp in timeStamps)
                    {
                        string newRow = string.Empty;
                        bool isUpdateAtType = false;
                        foreach (var asset in assets)
                        {

                            //if (mMultipleLog == null)
                            //{
                            //    mMultipleLog = asset.MultipleLog.Where(x => x.TimeStamp.Date == timeStamp.Date && x.TimeStamp.Hour == timeStamp.Hour && x.TimeStamp.Minute == timeStamp.Minute).LastOrDefault();
                            //}
                            if (asset.assetAttributes != null)
                            {
                                if (!isUpdateAtType)
                                {
                                    var assetTypeName = asset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();
                                    newRow += asset.SiteName + ",";// + row;
                                    newRow += timeStamp.ToString() + ",";
                                    newRow += assetTypeName + ",";
                                    isUpdateAtType = true;
                                }
                                if (asset.assetAttributes != null && asset.assetAttributes.Count > 0)
                                {
                                    foreach (var assetAttribute in asset.assetAttributes)
                                    {
                                        var mMultipleLog = rdpmsResponse.Data.Where(x => x.AssetId == asset.Id && x.AttributeId == assetAttribute.Id).FirstOrDefault();
                                        if (mMultipleLog != null && mMultipleLog.Values != null && mMultipleLog.Values.Count > 0)
                                        {
                                            var firstValue = mMultipleLog.Values.OrderBy(x => x.Value.Timestamp.TimestampDevice).FirstOrDefault();
                                            if (firstValue.Value != null && assetAttribute.Data.Count == 0)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(firstValue.Value.Value));
                                            }
                                            var changedValue = mMultipleLog.Values.Where(x => x.Value.Timestamp.TimestampDevice >= timeStamp.AddSeconds(-5) && x.Value.Timestamp.TimestampDevice <= timeStamp.AddSeconds(5)).FirstOrDefault();
                                            if (changedValue.Value != null)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(changedValue.Value.Value));
                                            }
                                        }

                                        if (assetAttribute.Data != null)
                                        {
                                            var data = assetAttribute.Data.FirstOrDefault();
                                            newRow += data + ",";
                                        }
                                        else
                                        {
                                            newRow += "0.0,";
                                        }
                                    }
                                }
                            }
                            else
                            {

                            }
                        }

                        if (isUpdateAtType)
                        {
                            //Add the Data rows.
                            csv += newRow.TrimEnd(',');
                            //Add new line.
                            csv += "\r\n";
                        }
                    }
                }

            }


            return csv;
        }

        public string PrepareHtml(List<Asset> assets)
        {
            string csv = string.Empty;
            if (assets != null && assets.Count > 0)
            {
                csv += "<table>";
                csv += "<tr><thead id='tblHeader' class='table-dark'>";
                csv += "<th>Site Name</th>";
                csv += "<th>Date</th>";
                csv += "<th>Asset Type</th>";


                foreach (var asset in assets)
                {
                    foreach (var assetAttribute in asset.assetAttributes)
                    {
                        csv += $"<th>{asset.Name} {assetAttribute.Title}</th>";
                    }
                }
                csv += "</tr></thead>";

                var mAssetAttributes = GetAll();

                var timeStamps = assets.SelectMany(x => x.MultipleLog).GroupBy(x => x.TimeStamp).Select(x => x.Key).ToList();
                csv += " <tbody style='max-height: 600px;overflow-y:auto;'>";
                foreach (var timeStamp in timeStamps)
                {
                    string newRow = "<tr>";
                    bool isUpdateAtType = false;
                    foreach (var asset in assets)
                    {
                        var mAllAssetAttributes = mAssetAttributes.Where(x => x.AssetTypeId == asset.AssetTypeId).ToList();

                        var mMultipleLog = asset.MultipleLog.Where(x => x.TimeStamp == timeStamp).FirstOrDefault();
                        if (mMultipleLog != null && mMultipleLog.TimeStamp != null && mMultipleLog.TimeStamp != DateTime.MinValue && asset.assetAttributes != null)
                        {
                            if (!isUpdateAtType)
                            {
                                var assetTypeName = asset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();
                                newRow += $"<td>{asset.SiteName}</td>";
                                newRow += $"<td>{timeStamp.ToString()}</td>";
                                newRow += $"<td>{assetTypeName}</td>";
                                isUpdateAtType = true;
                            }
                            var assetAttributes = GetGraphAttribute(asset.assetAttributes, mAllAssetAttributes, mMultipleLog.CsvData);
                            if (assetAttributes != null && assetAttributes.Count > 0)
                            {
                                foreach (var assetAttribute in assetAttributes)
                                {
                                    if (assetAttribute.Data != null)
                                    {
                                        var data = assetAttribute.Data.FirstOrDefault();

                                        newRow += $"<td>{data}</td>";
                                    }
                                    else
                                    {
                                        newRow += $"<td>0.0</td>";
                                    }
                                }
                            }
                        }
                        else
                        {

                        }
                    }


                    newRow += "</tr>";

                    csv += newRow;
                }
                csv += "</tbody>";

                csv += "</table>";
            }

            return csv;
        }
    }
}
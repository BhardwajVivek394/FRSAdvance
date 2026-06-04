using Domain;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using System.Collections.Generic;
using System;
using System.Linq;

namespace E7FRSAdvance.Service
{
    public class GraphService : IGraphService
    {
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
                                if (assetAttribute.AliasName.IsNotNullOrEmpty())
                                    assetAttribute.Title = assetAttribute.AliasName;

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
                            {
                                if (assetAttribute.AliasName.IsNotNullOrEmpty())
                                    assetAttribute.Title = assetAttribute.AliasName;

                                assetAttribute.DataArray.Add(Convert.ToString(array[i]));
                            }
                        }
                    }

                    i++;
                }
            }

        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace USStockDownloader.Utils
{
    /// <summary>
    /// キャッシュの状態を表す列挙型
    /// </summary>
    public enum CacheStatus
    {
        /// <summary>
        /// キャッシュが無効（存在しない、または期間が一致しない）
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// キャッシュが古い（最終更新日が古い）
        /// </summary>
        Outdated = 1,

        /// <summary>
        /// キャッシュが有効
        /// </summary>
        Valid = 2
    }
}

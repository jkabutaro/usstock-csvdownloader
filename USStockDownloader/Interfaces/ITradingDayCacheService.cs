using System;
using System.Threading.Tasks;

namespace USStockDownloader.Interfaces
{
    /// <summary>
    /// 取引日キャッシュサービスのインターフェース
    /// </summary>
    public interface ITradingDayCacheService
    {
        /// <summary>
        /// 指定された日付が取引日かどうかを確認します
        /// </summary>
        /// <param name="date">確認する日付</param>
        /// <returns>取引日の場合はtrue</returns>
        Task<bool> CheckTradingDayExistsAsync(DateTime date);

        /// <summary>
        /// 指定された日付範囲内に取引日が存在するかを確認します
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <returns>範囲内に取引日が存在する場合はtrue</returns>
        Task<bool> CheckTradingDayRangeAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// 指定された日付がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="date">確認する日付</param>
        /// <returns>データなし期間に含まれる場合はtrue</returns>
        bool IsDateInNoDataPeriod(string symbol, DateTime date);

        /// <summary>
        /// 指定された月全体がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="monthStart">確認する月の初日</param>
        /// <returns>月全体がデータなし期間に含まれる場合はtrue</returns>
        bool IsMonthFullyInNoDataPeriod(string symbol, DateTime monthStart);

        /// <summary>
        /// 指定された月をデータが存在しない期間として記録します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="monthStart">データが存在しない月の初日</param>
        void RecordNoDataPeriod(string symbol, DateTime monthStart);

        /// <summary>
        /// 指定された日付範囲をデータが存在しない期間として記録します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="startDate">データが存在しない期間の開始日</param>
        /// <param name="endDate">データが存在しない期間の終了日</param>
        void RecordNoDataPeriod(string symbol, DateTime startDate, DateTime endDate);

        /// <summary>
        /// キャッシュを保存します
        /// </summary>
        Task SaveCacheAsync();
        
        /// <summary>
        /// 従来のシンボルなしのIsDateInNoDataPeriodメソッド（後方互換性のため）
        /// </summary>
        /// <param name="date">確認する日付</param>
        /// <returns>データなし期間に含まれる場合はtrue</returns>
        bool IsDateInNoDataPeriod(DateTime date);
        
        /// <summary>
        /// 従来のシンボルなしのIsMonthFullyInNoDataPeriodメソッド（後方互換性のため）
        /// </summary>
        /// <param name="monthStart">確認する月の初日</param>
        /// <returns>月全体がデータなし期間に含まれる場合はtrue</returns>
        bool IsMonthFullyInNoDataPeriod(DateTime monthStart);
        
        /// <summary>
        /// 従来のシンボルなしのRecordNoDataPeriodメソッド（後方互換性のため）
        /// </summary>
        /// <param name="monthStart">データが存在しない月の初日</param>
        void RecordNoDataPeriod(DateTime monthStart);
        
        /// <summary>
        /// 従来のシンボルなしのRecordNoDataPeriodメソッド（後方互換性のため）
        /// </summary>
        /// <param name="startDate">データが存在しない期間の開始日</param>
        /// <param name="endDate">データが存在しない期間の終了日</param>
        void RecordNoDataPeriod(DateTime startDate, DateTime endDate);
    }
}

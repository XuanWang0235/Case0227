namespace ovbMidApi.Controllers;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvbRadzen.Models.AppDb;

[ApiController]
[Route("api/[controller]")]
public partial class DoneOkController : ControllerBase
{
    private readonly string logFilePath;
    private OvbRadzen.Data.AppDbContext _AppDbContext;
    public DoneOkController(OvbRadzen.Data.AppDbContext appDbContext)
    {
        // Set log file path relative to the bin folder
        logFilePath = Path.Combine(AppContext.BaseDirectory, "webhook_logs.txt");
        _AppDbContext = appDbContext;
    }



    /// <summary>
    /// NOTE by Mark, 10/17, 3D付款成功
    /// NOTE by Mark, 10/28, 不成功也會到這裡
    /// NOTE by Mark, 10/30, 判斷付款成功後立即開發票
    /// </summary>
    /// <param name="payload"></param>
    /// <returns></returns>
    [HttpPost]
    [Route("RedirectOk")]
    public async Task<IActionResult> RedirectOk([FromBody] RedirectRequest payload)
    {


        try
        {
            TimeZoneInfo taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
            DateTime taipeiTime = TimeZoneInfo.ConvertTime(DateTime.Now, taipeiTimeZone);

            var caseTime = taipeiTime;
            LogToFile("現在的系統時間:"+ caseTime);
            //caseTime = new DateTime(2025, 2, 28, 21, 0, 0, DateTimeKind.Unspecified);
            //caseTime = new DateTime(2025, 3, 5, 21, 0, 0, DateTimeKind.Unspecified);
            //caseTime = new DateTime(2025, 4, 7, 21, 0, 0, DateTimeKind.Unspecified);
            caseTime = TimeZoneInfo.ConvertTime(caseTime, taipeiTimeZone);

            LogToFile("調整過的系統時間,模擬上線後的真實時間:"+ caseTime);
            var switchTime = new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Unspecified);

            // 一般會員也是會先有記錄才能購買的
            var vip = await _AppDbContext.WEB_MEMBER
                                     .Where(a => a.MEMBER_SN == payload.RecordStatus)
                                     .FirstOrDefaultAsync();
            vip.MEMBER_VIP_TYPE = 3;
            if (caseTime >switchTime)//如果大於切換時間套用一年
            {
                vip.MEMBER_VIP_EXPIRY_DATE = DateTime.Now.AddYears(1); await _AppDbContext.SaveChangesAsync();
                LogToFile($"VIP有效日期{vip.MEMBER_VIP_EXPIRY_DATE.ToString("yyyy-MM-dd")}");
            }
            else//如果小於切換時間仍然有效到 2026-03-01
            {
                // 一般會員也是會先有記錄才能購買的
                vip.MEMBER_VIP_EXPIRY_DATE = DateTime.Parse("2026-03-01"); await _AppDbContext.SaveChangesAsync();
                LogToFile($"VIP有效日期{vip.MEMBER_VIP_EXPIRY_DATE.ToString("yyyy-MM-dd")}");
            }



            // NOTE by Mark, 10/27, 年訂閱會員 新增一筆 [DISCORD_EVENT], 
            // 讓 bot 去加 role
            //var discordUser = await _AppDbContext.DISCORD_USER.Where(a => a.DISCORD_USER_SN == objTrade.MEMBER_SN).FirstOrDefaultAsync();

            var discordUser = await _AppDbContext.DISCORD_USER.Where(a => "" + a.DISCORD_USER_ID == vip.MEMBER_BIND_DISCORD_ID).FirstOrDefaultAsync();
            if (discordUser != null)
            {
                var newEvent = new DISCORD_EVENT();
                newEvent.DISCORD_EVENT_TYPE = 1;//(2.0訂閱學員,markplchen), added successfully! |
                newEvent.DISCORD_EVENT_STATE = 0;//0 for bot TODO
                newEvent.WEB_SOMETHING_SN = payload.RecordStatus;//[WEB_MEMBER].[MEMBER_SN]
                                                                 //newEvent.MEMBER_BIND_DISCORD_ID = discordUser.DISCORD_USER_ID;//[DISCORD_USER].[DISCORD_USER_ID]
                                                                 // MARKTODO 待優化
                newEvent.MEMBER_BIND_DISCORD_ID = "" + discordUser.DISCORD_USER_ID;//[DISCORD_USER].[DISCORD_USER_ID]
                newEvent.DISCORD_CHANNEL_ID = 0;
                newEvent.ADD_DATE = DateTime.Now;
                newEvent.FINISH_DATE = DateTime.Now;   // MARKTODO 待優化
                newEvent.DISCORD_EVENT_LOG = ".";// MARKTODO 待優化
                newEvent.DISCORD_EVENT_MSG = ".";// MARKTODO 待優化
                _AppDbContext.Add(newEvent);
                await _AppDbContext.SaveChangesAsync();

            }


            // Return a success response
            return Ok();
        }
        catch (Exception ex)
        {
            LogToFile($"Error processing RedirectOk for tradeId {payload.TradeId}: {ex.Message}");
            return StatusCode(500, "An error occurred! " + ex.Message);
        }
    }

    [HttpPost]
    [Route("/api/RedirectOkXXX")]
    //public async Task<IActionResult> RedirectOkAsync([FromBody] string tradeId)
    public async Task<IActionResult> RedirectOkXXX([FromBody] RedirectRequest payload)
    {


        try
        {
            TimeZoneInfo taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
            DateTime taipeiTime = TimeZoneInfo.ConvertTime(DateTime.Now, taipeiTimeZone);
            // Validate input
            if (string.IsNullOrEmpty(payload.TradeId))
            {
                LogToFile("TradeId is missing.");
                return BadRequest("TradeId is required.");
            }

            // Find the record in the database
            var objOms = await _AppDbContext.WEB_TRADE_OMS
                                         .Where(a => a.OMS_ID == payload.TradeId)
                                         .FirstOrDefaultAsync();

            // NOTE by Mark, 10/17, 填上 D3 信息
            if (objOms != null)
            {
                // Update fields
                objOms.D3_DATE = taipeiTime;
                //obj.D3_RECORD_STATUS = 4; // Assuming you set a specific status for this action
                objOms.D3_RECORD_STATUS = payload.RecordStatus; // Assuming you set a specific status for this action

                // Attempt to save changes to the database

                await _AppDbContext.SaveChangesAsync();
                LogToFile($"Changes saved to DB for tradeId: {payload.TradeId}.");

                // NOTE by Mark, 10/28, 4 is OK, 2 is NOT OK
                if (objOms.D3_RECORD_STATUS != 4)
                {
                    LogToFile($"Transaction failed for tradeId: {payload.TradeId}");
                    // 添加失敗處理邏輯，例如更新交易狀態或通知相關人員
                    return StatusCode(400, "Transaction not successful 交易失敗!");
                }


                // 付款成功後更新額外的資訊
                var objTrade = await _AppDbContext.WEB_TRADE.Where(a => a.TRADE_SN == objOms.TRADE_SN).FirstOrDefaultAsync();

                // NOTE by Mark, MARKTODO 2.6, 後續應該在此開發票, 綜合考量, 現階段先仿 Discord bot 方式，獨立查看執行



                #region 更新會員 MEMBER_VIP_TYPE
                if (objTrade != null)
                {
                    // 一般會員也是會先有記錄才能購買的
                    var vip = await _AppDbContext.WEB_MEMBER
                                             .Where(a => a.MEMBER_SN == objTrade.MEMBER_SN)
                                             .FirstOrDefaultAsync();
                    if (vip != null)
                    {
                        if (objTrade.ITEM_NAME == "OVERBANKROLL 年訂閱會員") // NOTE by Mark, 10/16 11:30, together with Y!
                        {
                            vip.MEMBER_VIP_TYPE = 3;
                            vip.MEMBER_VIP_EXPIRY_DATE = DateTime.Parse("2026-03-01"); await _AppDbContext.SaveChangesAsync();

                            // NOTE by Mark, 10/27, 年訂閱會員 新增一筆 [DISCORD_EVENT], 
                            // 讓 bot 去加 role
                            //var discordUser = await _AppDbContext.DISCORD_USER.Where(a => a.DISCORD_USER_SN == objTrade.MEMBER_SN).FirstOrDefaultAsync();

                            var discordUser = await _AppDbContext.DISCORD_USER.Where(a => "" + a.DISCORD_USER_ID == vip.MEMBER_BIND_DISCORD_ID).FirstOrDefaultAsync();
                            if (discordUser != null)
                            {
                                var newEvent = new DISCORD_EVENT();
                                newEvent.DISCORD_EVENT_TYPE = 1;//(2.0訂閱學員,markplchen), added successfully! |
                                newEvent.DISCORD_EVENT_STATE = 0;//0 for bot TODO
                                newEvent.WEB_SOMETHING_SN = objTrade.MEMBER_SN;//[WEB_MEMBER].[MEMBER_SN]
                                                                               //newEvent.MEMBER_BIND_DISCORD_ID = discordUser.DISCORD_USER_ID;//[DISCORD_USER].[DISCORD_USER_ID]
                                                                               // MARKTODO 待優化
                                newEvent.MEMBER_BIND_DISCORD_ID = "" + discordUser.DISCORD_USER_ID;//[DISCORD_USER].[DISCORD_USER_ID]
                                newEvent.DISCORD_CHANNEL_ID = 0;
                                newEvent.ADD_DATE = DateTime.Now;
                                newEvent.FINISH_DATE = DateTime.Now;   // MARKTODO 待優化
                                newEvent.DISCORD_EVENT_LOG = ".";// MARKTODO 待優化
                                newEvent.DISCORD_EVENT_MSG = ".";// MARKTODO 待優化
                                _AppDbContext.Add(newEvent);
                                await _AppDbContext.SaveChangesAsync();

                            }
                        }
                        // --- 吉祥大禮包
                        else if (objTrade.ITEM_NAME == "吉祥大禮包") // NOTE by Mark, 10/17
                        {
                            OvbRadzen.Models.AppDb.MEMBER_PACKAGE package = new OvbRadzen.Models.AppDb.MEMBER_PACKAGE();
                            package.TRADE_NO = objTrade.TRADE_NO;
                            package.MEMBER_SN = objTrade.MEMBER_SN;
                            package.PACKAGE_SN = 1;
                            package.ENABLE = true;
                            package.ADD_DATE = taipeiTime;
                            package.UPDATE_DATE = taipeiTime;
                            package.NOTE = "91金流";
                            _AppDbContext.Add(package);
                            await _AppDbContext.SaveChangesAsync();

                            // NOTE by Mark, 10/27, 為配合只買 吉祥大禮包 但未年訂閱的會員 要能在 Web 看 基礎教材
                            //                      以 [WEB_MEMBER][MEMBER_VIP_TYPE]=-99 來做區分


                            vip.MEMBER_VIP_TYPE = -99;
                            await _AppDbContext.SaveChangesAsync();
                        }
                        else if (objTrade.ITEM_NAME == "體驗課") // NOTE by Mark, 10/28
                        {
                            OvbRadzen.Models.AppDb.MEMBER_PACKAGE package = new OvbRadzen.Models.AppDb.MEMBER_PACKAGE();
                            package.TRADE_NO = objTrade.TRADE_NO;
                            package.MEMBER_SN = objTrade.MEMBER_SN;
                            package.PACKAGE_SN = 2; // NOTE by Mark, 最小程度做的 增加產品
                            package.ENABLE = true;
                            package.ADD_DATE = taipeiTime;
                            package.UPDATE_DATE = taipeiTime;
                            package.NOTE = "91金流 要準備對照組 TEST vs PROD";
                            _AppDbContext.Add(package);
                            await _AppDbContext.SaveChangesAsync();
                        }
                    }
                }

                #endregion
            }
            else
            {
                LogToFile($"Record with tradeId {payload.TradeId} not found.");
                return NotFound("Record not found");
            }

            LogToFile($"RedirectOk processed for tradeId: {payload.TradeId}");

            // Return a success response
            return Ok();
        }
        catch (Exception ex)
        {
            LogToFile($"Error processing RedirectOk for tradeId {payload.TradeId}: {ex.Message}");
            return StatusCode(500, "An error occurred! " + ex.Message);
        }
    }

    [HttpPost]
    [Route("Callback")]
    public async Task<IActionResult> PostAsync([FromBody] WebhookPayload payload)
    {
        TimeZoneInfo taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        DateTime taipeiTime = TimeZoneInfo.ConvertTime(DateTime.Now, taipeiTimeZone);

        try
        {
            var tradeId = payload.TradeId;
            var recordStatus = payload.RecordStatus;
            var x = payload.MerchantOrderId;

            var obj = await _AppDbContext.WEB_TRADE_OMS
                                         .Where(a => a.OMS_ID == tradeId)
                                         .FirstOrDefaultAsync();

            if (obj != null)
            {
                obj.D3_DATE = taipeiTime;
                obj.D3_RECORD_STATUS = recordStatus;

                // Attempt to save changes to the database
                try
                {
                    await _AppDbContext.SaveChangesAsync();
                    LogToFile("Changes saved to DB successfully.");
                }
                catch (Exception dbEx)
                {
                    LogToFile($"Database save error: {dbEx.Message}");
                    return StatusCode(500, "Database save error occurred");
                }
            }
            // https://developer.91app.com/zh-tw/apis/admin-payments/#tag/Payments/operation/post-notify-endpoint
            // 91APP Payments 在交易完成後會另外提供一個 webhook 通知商店端，以避免 3D 交易成功但後續導頁中斷等情境使得商店沒收到交易結果
            LogToFile($"交易完成 tradeId: {payload.TradeId}, recordStatus: {payload.RecordStatus}, merchantOrderId: {payload.MerchantOrderId}");

            // Return a success response
            return Ok();
        }
        catch (Exception ex)
        {
            LogToFile($"Error: {ex.Message}");
            return StatusCode(500, "An error occurred");
        }
    }



    private void LogToFile(string message)
    {
        using (StreamWriter sw = new StreamWriter(logFilePath, true))
        {
            sw.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}: {message}");
        }
    }
}

// Webhook payload model
public class WebhookPayload
{
    public string TradeId { get; set; }
    public int RecordStatus { get; set; }
    public string MerchantOrderId { get; set; }
}

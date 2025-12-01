using System.Diagnostics;
using System.Text.Json;
using Making.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Thor.Abstractions.Exceptions;
using Thor.Abstractions.Responses;
using Thor.Abstractions.Responses.Dto;
using Thor.Abstractions.Responses.Input;
using Thor.Domain.Chats;
using Thor.Domain.System;
using Thor.Infrastructure;
using Thor.Service.Domain.Core;
using Thor.Service.Extensions;

namespace Thor.Service.Service.AI;

/// <summary>
/// OpenAI Responses Service
/// </summary>
/// <param name="serviceProvider"></param>
[MiniApi("/v1")]
public sealed class ResponsesService(
    IServiceProvider serviceProvider,
    RateLimitModelService rateLimitModelService,
    TokenService tokenService,
    ILogger<ResponsesService> logger,
    ModelMapService modelMapService,
    ChannelService channelService,
    UserGroupService userGroupService,
    LoggerService loggerService,
    UserService userService,
    ImageService imageService,
    SubscriptionService subscriptionService
)
    : AIService(serviceProvider, imageService)
{
    /// <summary>
    /// 检查并处理用户额度（优先使用套餐额度，不足时使用用户余额）
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <param name="model">模型名称</param>
    /// <param name="quota">需要消费的额度</param>
    /// <returns>是否成功，使用的额度类型，错误信息，套餐分组</returns>
    private async Task<(bool success, bool usedSubscription, string? errorMessage, string[]? subscriptionGroups)>
        CheckAndValidateQuotaAsync(User user, string model, long quota)
    {
        // 1. 先检查用户是否有有效套餐
        var subscription = await subscriptionService.GetUserActiveSubscriptionAsync(user.Id);

        if (subscription?.Plan != null)
        {
            // 2. 检查套餐是否支持该模型
            if (subscription.Plan.IsModelAllowed(model))
            {
                // 3. 检查套餐额度是否充足
                var (hasSufficient, reason) = await subscriptionService.CheckQuotaSufficientAsync(user.Id, quota);
                if (hasSufficient)
                {
                    return (true, true, null, subscription.Plan.Groups); // 使用套餐额度，返回套餐分组
                }

                // 套餐额度不足，尝试使用用户余额
                if (quota <= user.ResidualCredit)
                {
                    return (true, false, null, null); // 使用用户余额
                }

                return (false, false, "套餐额度不足且用户余额不足，请充值或升级套餐", null);
            }
        }

        // 4. 没有套餐或套餐不支持该模型，检查用户余额
        if (quota <= user.ResidualCredit)
        {
            return (true, false, null, null); // 使用用户余额
        }

        // 5. 根据是否有套餐给出不同提示
        if (subscription?.Plan != null)
        {
            return (false, false, "当前套餐不支持该模型且用户余额不足，请充值或升级套餐", null);
        }

        return (false, false, "账号余额不足请充值", null);
    }

    /// <summary>
    /// 消费额度（优先消费套餐额度）
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <param name="model">模型名称</param>
    /// <param name="quota">消费的额度</param>
    /// <param name="requestTokens">请求token数</param>
    /// <param name="responseTokens">响应token数</param>
    /// <param name="usedSubscription">是否使用了套餐额度</param>
    /// <param name="tokenKey">token key</param>
    /// <param name="channelId">渠道ID</param>
    /// <param name="context">HTTP上下文</param>
    private async Task ConsumeQuotaAsync(User user, string model, long quota, int requestTokens, int responseTokens,
        bool usedSubscription, string? tokenKey, string channelId, HttpContext context)
    {
        if (usedSubscription)
        {
            // 使用套餐额度
            await subscriptionService.ConsumeQuotaAsync(
                user.Id, model, quota, requestTokens, responseTokens,
                context.GetIpAddress(), context.GetUserAgent());
        }
        else
        {
            // 使用用户余额
            await userService.ConsumeAsync(user.Id, quota, requestTokens, tokenKey, channelId, model);
        }
    }

    [HttpPost("/responses")]
    public async Task ExecuteAsync(HttpContext context, ResponsesInput request)
    {
        using var chatCompletions =
            Activity.Current?.Source.StartActivity("对话补全调用");

        // 记录完整的请求参数
        try
        {
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            logger.LogInformation("responses 对话补全请求完整参数：{RequestJson}", requestJson);
        }
        catch (Exception serializeEx)
        {
            logger.LogWarning("序列化请求参数失败：{SerializeEx}", serializeEx.Message);
        }

        var model = request.Model;

        if (request.Model?.StartsWith("gpt-5-codex") == true)
        {
            request.Store = false;
            request.ServiceTier = null;
        }

        var rateLimit = 0;
        Exception? exception = null;
        bool header = false;

        // 用于限流重试，如果限流则重试并且进行重新负载均衡计算
        limitGoto:

        try
        {
            var organizationId = string.Empty;
            if (context.Request.Headers.TryGetValue("OpenAI-Organization", out var organizationIdHeader))
            {
                organizationId = organizationIdHeader.ToString();
            }

            if (ModelManagerService.PromptRate.TryGetValue(request.Model, out var rate))
            {
                // 首先不检查Token额度获取用户信息，以便检查套餐状态
                var (token, user) = await tokenService.CheckTokenAsync(context, rate, false);

                // 检查用户是否有套餐，获取套餐分组信息
                var subscription = await subscriptionService.GetUserActiveSubscriptionAsync(user.Id);
                bool hasValidSubscription =
                    subscription?.Plan != null && subscription.Plan.IsModelAllowed(request.Model);

                // 如果没有有效套餐支持该模型，则需要检查Token额度和用户余额
                if (!hasValidSubscription)
                {
                    // 重新进行完整的Token检查（包括Token额度）
                    (token, user) = await tokenService.CheckTokenAsync(context, rate, true);
                }
                else
                {
                    // 请求前先校验套餐额度当天是否充足
                    if (subscription!.Plan!.DailyQuotaLimit > 0 &&
                        subscription.DailyUsedQuota >= subscription.Plan.DailyQuotaLimit)
                    {
                        context.Response.StatusCode = 402;
                        await context.WriteErrorAsync("当前套餐日额度已用尽，请明天再试或升级套餐", "402");
                        return;
                    }

                    // 请求前先校验套餐额度每周是否充足
                    if (subscription!.Plan!.WeeklyQuotaLimit > 0 &&
                        subscription.WeeklyUsedQuota >= subscription.Plan.WeeklyQuotaLimit)
                    {
                        context.Response.StatusCode = 402;
                        await context.WriteErrorAsync("当前套餐周额度已用尽，请下周再试或升级套餐", "402");
                        return;
                    }
                }

                await rateLimitModelService.CheckAsync(request.Model, user.Id);

                TokenService.CheckModel(request.Model, token, context);

                request.Model = await modelMapService.ModelMap(request.Model);

                string[]? subscriptionGroups = null;
                if (hasValidSubscription)
                {
                    subscriptionGroups = subscription!.Plan!.Groups;
                }

                // 获取渠道通过算法计算权重（优先使用套餐分组）
                var channel =
                    CalculateWeight(
                        await channelService.GetChannelsContainsModelWithSubscriptionGroupAsync(request.Model, user,
                            token, subscriptionGroups, true));

                if (channel == null && exception == null)
                    throw new NotModelException(
                        $"{request.Model}在分组：{(token?.Groups.FirstOrDefault() ?? user.Groups.FirstOrDefault())} 未找到可用渠道");

                if (channel == null && exception != null)
                {
                    await context.WriteErrorAsync(exception.Message, "400");
                    return;
                }

                var userGroup = await userGroupService.GetAsync(channel.Groups);

                if (userGroup == null)
                {
                    throw new BusinessException("当前渠道未设置分组，请联系管理员设置分组", "400");
                }

                // 获取渠道指定的实现类型的服务
                var chatCompletionsService = GetKeyedService<IThorResponsesService>(channel.Type);

                if (chatCompletionsService == null)
                {
                    throw new BusinessException($"并未实现：{channel.Type} 的服务", "400");
                }


                // 记录请求模型 / 请求用户
                logger.LogInformation("请求模型：{model} 请求用户：{user} 请求分配渠道 ：{name}", model, user?.UserName,
                    channel.Name);

                int requestToken;
                var responseToken = 0;
                int? cachedTokens = null;

                var sw = Stopwatch.StartNew();

                // 如果使用套餐额度，添加响应头
                if (hasValidSubscription && !header)
                {
                    AddSubscriptionUsageHeaders(context, subscription!);
                    header = true;
                }

                if (request.Stream == true)
                {
                    using var activity =
                        Activity.Current?.Source.StartActivity("流式对话", ActivityKind.Internal);

                    (requestToken, responseToken, cachedTokens) =
                        await StreamHandlerAsync(context, request, channel, chatCompletionsService);
                }
                else
                {
                    using var activity =
                        Activity.Current?.Source.StartActivity("非流式对话", ActivityKind.Internal);

                    (requestToken, responseToken, cachedTokens) =
                        await ChatHandlerAsync(context, request, channel, chatCompletionsService);
                }

                var quota = requestToken * rate.PromptRate;

                var completionRatio = rate.CompletionRate ?? GetCompletionRatio(model);
                quota += responseToken * rate.PromptRate * completionRatio;

                // 计算分组倍率
                quota = (decimal)userGroup!.Rate * quota;

                // 将quota 四舍五入
                quota = Math.Round(quota, 0, MidpointRounding.AwayFromZero);

                sw.Stop();

                // 判断是否按次
                if (rate.QuotaType == ModelQuotaType.OnDemand)
                {
                    // 如果命中缓存 并且缓存倍率大于0 小于0则不计算缓存
                    if (cachedTokens > 0 && rate.CacheRate > 0)
                    {
                        // 未命中缓存的token按原价计费
                        var uncachedRequestTokens = requestToken - cachedTokens.Value;
                        quota = uncachedRequestTokens * rate.PromptRate;

                        // 命中缓存的token按缓存价格计费
                        quota += cachedTokens.Value * rate.CacheRate.Value;

                        // 响应token按缓存价格计费
                        quota += responseToken * rate.CacheRate.Value * completionRatio;

                        // 计算分组倍率
                        quota = (decimal)userGroup!.Rate * quota;

                        // 将quota 四舍五入
                        quota = Math.Round(quota, 0, MidpointRounding.AwayFromZero);

                        // 计算节省百分比
                        var cacheHitRate = rate.CacheHitRate ?? 0.25m;
                        var savingsPercent = Math.Round((1 - cacheHitRate) * 100, 2);

                        // 准备内容，记录是否使用套餐
                        var content = string.Format(ConsumerTemplateCache, rate.PromptRate, completionRatio,
                            userGroup.Rate,
                            cachedTokens, cacheHitRate, savingsPercent);
                        if (hasValidSubscription)
                        {
                            var userSubscription = await subscriptionService.GetUserActiveSubscriptionAsync(user!.Id);
                            if (userSubscription?.Plan != null)
                            {
                                content += $" 扣费来源：套餐额度({userSubscription.Plan.Name})";
                            }
                        }
                        else
                        {
                            content += " 扣费来源：用户余额";
                        }

                        await loggerService.CreateConsumeAsync("/v1/responses",
                            content,
                            model,
                            requestToken, responseToken, (int)quota, token?.Key, user?.UserName, user?.Id, channel.Id,
                            channel.Name, context.GetIpAddress(), context.GetUserAgent(),
                            request.Stream is true,
                            (int)sw.ElapsedMilliseconds, organizationId);
                    }
                    else
                    {
                        // 准备内容，记录是否使用套餐
                        var content = string.Format(ConsumerTemplate, rate.PromptRate, completionRatio, userGroup.Rate);
                        if (hasValidSubscription)
                        {
                            var userSubscription = await subscriptionService.GetUserActiveSubscriptionAsync(user!.Id);
                            if (userSubscription?.Plan != null)
                            {
                                content += $" 扣费来源：套餐额度({userSubscription.Plan.Name})";
                            }
                        }
                        else
                        {
                            content += " 扣费来源：用户余额";
                        }

                        await loggerService.CreateConsumeAsync("/v1/responses",
                            content,
                            model,
                            requestToken, responseToken, (int)quota, token?.Key, user?.UserName, user?.Id, channel.Id,
                            channel.Name, context.GetIpAddress(), context.GetUserAgent(),
                            request.Stream is true,
                            (int)sw.ElapsedMilliseconds, organizationId);
                    }

                    await ConsumeQuotaAsync(user!, model, (long)quota, requestToken, responseToken,
                        hasValidSubscription, token?.Key, channel.Id, context);
                }
                else
                {
                    // 准备内容，记录是否使用套餐
                    var content = string.Format(ConsumerTemplateOnDemand, RenderHelper.RenderQuota(rate.PromptRate),
                        userGroup.Rate);
                    if (hasValidSubscription)
                    {
                        var userSubscription = await subscriptionService.GetUserActiveSubscriptionAsync(user!.Id);
                        if (userSubscription?.Plan != null)
                        {
                            content += $" 扣费来源：套餐额度({userSubscription.Plan.Name})";
                        }
                    }
                    else
                    {
                        content += " 扣费来源：用户余额";
                    }

                    // 费用
                    await loggerService.CreateConsumeAsync("/v1/responses",
                        content,
                        model,
                        requestToken, responseToken, (int)((int)rate.PromptRate * (decimal)userGroup.Rate), token?.Key,
                        user?.UserName, user?.Id,
                        channel.Id,
                        channel.Name, context.GetIpAddress(), context.GetUserAgent(),
                        request.Stream is true,
                        (int)sw.ElapsedMilliseconds, organizationId);

                    await ConsumeQuotaAsync(user!, model, (int)((int)rate.PromptRate * (decimal)userGroup.Rate),
                        requestToken, responseToken,
                        hasValidSubscription, token?.Key, channel.Id, context);
                }
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.WriteErrorAsync($"当前{request.Model}模型未设置倍率,请联系管理员设置倍率", "400");
            }
        }
        catch (ThorRateLimitException)
        {
            exception = new ThorRateLimitException("对话模型请求限流，请稍后再试");
            logger.LogWarning("对话模型请求限流：{rateLimit}", rateLimit);
            rateLimit++;
            // TODO：限流重试次数
            if (rateLimit > 10)
            {
                context.Response.StatusCode = 429;
            }
            else
            {
                request.Model = model;
                goto limitGoto;
            }
        }
        catch (InsufficientQuotaException insufficientQuotaException)
        {
            if (context.Response.StatusCode != 402)
            {
                context.Response.StatusCode = 402;
            }

            await context.WriteErrorAsync(insufficientQuotaException.Message, "402");
        }
        catch (RateLimitException)
        {
            context.Response.StatusCode = 429;
        }
        catch (UnauthorizedAccessException e)
        {
            context.Response.StatusCode = 401;
        }
        catch (OpenAIErrorException error)
        {
            context.Response.StatusCode = 400;
            await context.WriteErrorAsync(error.Message, error.Code);
        }
        catch (NotModelException modelException)
        {
            context.Response.StatusCode = 400;
            await context.WriteErrorAsync(modelException.Message, "400");
        }
        catch (Exception e)
        {
            exception = e;
            logger.LogError("对话模型请求异常：{e} 准备重试{rateLimit}，请求参数：{request}", e, rateLimit,
                JsonSerializer.Serialize(request, ThorJsonSerializer.DefaultOptions));
            rateLimit++;
            // TODO：限流重试次数
            if (rateLimit > 5)
            {
                context.Response.StatusCode = 400;
                await context.WriteErrorAsync(e.Message, "500");
            }
            else
            {
                request.Model = model;
                goto limitGoto;
            }
        }
    }

    private async Task<(int requestToken, int responseToken, int? cachedTokens)>
        ChatHandlerAsync(HttpContext context,
            ResponsesInput request, ChatChannel channel, IThorResponsesService responsesService)
    {
        int requestToken = 0;
        int responseToken = 0;

        // 命中缓存tokens数量
        int? cachedTokens = null;

        var platformOptions = new ThorPlatformOptions(channel.Address, channel.Key, channel.Other);

        var result = await responsesService.GetResponseAsync(request, platformOptions);

        await context.Response.WriteAsJsonAsync(result, ThorJsonSerializer.DefaultOptions);

        cachedTokens = result?.Usage?.InputTokensDetails?.CachedTokens;

        requestToken = result?.Usage?.InputTokens ?? 0;

        responseToken = result?.Usage?.OutputTokens ?? 0;


        return (requestToken, responseToken, cachedTokens);
    }

    private async Task<(int requestToken, int responseToken, int? cachedTokens)>
        StreamHandlerAsync(HttpContext context,
            ResponsesInput request, ChatChannel channel, IThorResponsesService responsesService)
    {
        int requestToken = TokenHelper.GetTokens(request.Instructions ?? string.Empty);
        int responseToken = 0;

        // 命中缓存tokens数量
        int? cachedTokens = 0;

        var platformOptions = new ThorPlatformOptions(channel.Address, channel.Key, channel.Other);

        // 是否第一次输出
        bool isFirst = true;
        await foreach (var (@event, item) in responsesService.GetResponsesAsync(request, platformOptions))
        {
            if (isFirst)
            {
                context.SetEventStreamHeaders();
                isFirst = false;
            }

            if (item?.Response?.Output != null)
            {
                foreach (var output in item.Response.Output)
                {
                    if (output.Content is { Length: > 0 })
                    {
                        // 计算输出的token数量
                        responseToken += TokenHelper.GetTotalTokens(output.Content.Select(x => x.Text).ToArray());
                    }
                }
            }


            responseToken = TokenHelper.GetTotalTokens(item?.Delta ?? string.Empty);

            if (@event.Equals("response.completed"))
            {
                if (item?.Response?.Usage?.InputTokens > 0)
                {
                    requestToken = item.Response.Usage.InputTokens;
                }

                if (item?.Response?.Usage?.OutputTokens > 0)
                {
                    responseToken = item.Response.Usage.OutputTokens;
                }

                if (item?.Response?.Usage?.InputTokensDetails?.CachedTokens > 0)
                {
                    cachedTokens = item.Response.Usage.InputTokensDetails.CachedTokens;
                }
            }

            await context.WriteAsEventStreamDataAsync(@event, item).ConfigureAwait(false);
        }

        return (requestToken, responseToken, cachedTokens);
    }

    /// <summary>
    /// 添加套餐使用情况的响应头信息
    /// </summary>
    /// <param name="context">HTTP上下文</param>
    /// <param name="userSubscription">用户订阅信息</param>
    private static void AddSubscriptionUsageHeaders(HttpContext context, UserSubscription userSubscription)
    {
        if (userSubscription?.Plan == null) return;

        var now = DateTime.Now;
        var plan = userSubscription.Plan;

        // 计算日配额使用百分比
        var dailyUsedPercent = plan.DailyQuotaLimit > 0
            ? Math.Min(100, (int)Math.Round((double)userSubscription.DailyUsedQuota / plan.DailyQuotaLimit * 100))
            : 0;

        // 计算周配额使用百分比
        var weeklyUsedPercent = plan.WeeklyQuotaLimit > 0
            ? Math.Min(100, (int)Math.Round((double)userSubscription.WeeklyUsedQuota / plan.WeeklyQuotaLimit * 100))
            : 0;

        // 计算日重置时间（基于实际的 LastDailyResetDate 和重置逻辑）
        // 重置条件：subscription.LastDailyResetDate.Date < now.Date
        var nextDailyReset = userSubscription.LastDailyResetDate.Date < now.Date
            ? now.Date // 如果已经需要重置，则当前就是重置时间
            : now.Date.AddDays(1); // 否则下一个 UTC 日期 00:00:00
        var dailyResetSeconds = (int)(nextDailyReset - now).TotalSeconds;

        // 计算周重置时间（基于实际的 LastWeeklyResetDate 和重置逻辑）
        // 重置条件：subscription.LastWeeklyResetDate.Date < GetWeekStart(now)
        var currentWeekStart = GetWeekStart(now);
        var nextWeeklyReset = userSubscription.LastWeeklyResetDate.Date < currentWeekStart
            ? currentWeekStart // 如果已经需要重置，则当前周开始就是重置时间
            : currentWeekStart.AddDays(7); // 否则下周一 00:00:00
        var weeklyResetSeconds = (int)(nextWeeklyReset - now).TotalSeconds;

        // 计算超过次要限制的百分比（如果日配额超过了周配额的日均值）
        var dailyAverageLimit = plan.WeeklyQuotaLimit / 7.0;
        var overSecondaryLimitPercent = dailyAverageLimit > 0
            ? Math.Max(0,
                (int)Math.Round((userSubscription.DailyUsedQuota - dailyAverageLimit) / dailyAverageLimit * 100))
            : 0;

        // 添加响应头
        context.Response.Headers["x-codex-primary-used-percent"] = dailyUsedPercent.ToString();
        context.Response.Headers["x-codex-secondary-used-percent"] = weeklyUsedPercent.ToString();
        context.Response.Headers["x-codex-primary-window-minutes"] = "1440"; // 24小时 = 1440分钟
        context.Response.Headers["x-codex-secondary-window-minutes"] = "10080"; // 7天 = 10080分钟
        context.Response.Headers["x-codex-primary-reset-after-seconds"] = dailyResetSeconds.ToString();
        context.Response.Headers["x-codex-secondary-reset-after-seconds"] = weeklyResetSeconds.ToString();
        context.Response.Headers["x-codex-primary-over-secondary-limit-percent"] = overSecondaryLimitPercent.ToString();
    }

    /// <summary>
    /// 获取周开始时间（周一）
    /// </summary>
    /// <param name="date"></param>
    /// <returns></returns>
    private static DateTime GetWeekStart(DateTime date)
    {
        var days = (int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1; // 周一为0
        return date.Date.AddDays(-days);
    }
}
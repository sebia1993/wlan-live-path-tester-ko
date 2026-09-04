using System.Globalization;
using System.Net;
using System.Text;

namespace WlanLivePathTester.Core.Reporting;

internal static class LocalHtmlReportRenderer
{
    internal static string Render(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 48 * 1024);
        AppendDocumentStart(builder, report);
        AppendPrivacyNotice(builder, report.Metadata);
        AppendSummaryGrid(builder, report);
        AppendStructuredMeasurements(
            builder,
            report.StructuredMeasurements
                ?? Array.Empty<ReportMeasurementSection>());
        AppendLegacyMeasurementSection(builder, report.Measurements);
        AppendObservationSection(builder, report.BrowserObservation);
        AppendFindingSection(builder, report.Findings);
        AppendLimitations(builder, report.Limitations);
        builder.Append("<footer class=\"small footer\">이 파일은 WLAN Live Path Tester KO가 현재 PC에서 생성했습니다.</footer>");
        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private static void AppendDocumentStart(
        StringBuilder builder,
        LocalDiagnosticReport report)
    {
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>WLAN Live Path Tester KO 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}");
        builder.Append("main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:15px;margin:18px 0 8px}");
        builder.Append(".sub{color:#566573;margin-top:6px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:14px}");
        builder.Append(".card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}");
        builder.Append(".measure{border:1px solid #e0e5e9;border-radius:10px;padding:14px;margin-top:14px;background:#fbfcfc}");
        builder.Append("table{width:100%;border-collapse:collapse}th,td{padding:9px 10px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}th{color:#566573;font-weight:600}");
        builder.Append(".kv th{width:34%}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}");
        builder.Append(".badge.warn{background:#fff3cd;color:#7d6608}.badge.critical{background:#fdecea;color:#922b21}.badge.info{background:#e8f6f3;color:#0e6251}.badge.low{background:#fdecea;color:#922b21}.badge.medium{background:#fff3cd;color:#7d6608}.badge.high{background:#e8f6f3;color:#0e6251}");
        builder.Append("pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f7f9fa;border-radius:8px;padding:12px;margin:0}");
        builder.Append(".finding{border-left:4px solid #5dade2;padding-left:12px;margin-top:14px}.finding.warning{border-color:#f5b041}.finding.critical{border-color:#e74c3c}");
        builder.Append(".samples{max-height:520px;overflow:auto}.small{font-size:12px;color:#707b7c}.privacy{background:#fff8e7;border-color:#e8ce8a}.footer{margin-top:18px}.muted{color:#707b7c}.nowrap{white-space:nowrap}");
        builder.Append("@media(max-width:640px){main{padding:16px}.kv th{width:42%}.samples{overflow:auto}.grid{display:block}}");
        builder.Append("@media print{body{background:#fff}.card,.measure{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}.samples{max-height:none;overflow:visible}}");
        builder.Append("</style></head><body><main><header><h1>WLAN Live Path Tester KO</h1><div class=\"sub\">로컬 네트워크 진단 보고서 · ");
        Html(
            builder,
            report.Metadata.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append("</div></header>");
    }

    private static void AppendPrivacyNotice(
        StringBuilder builder,
        ReportMetadata metadata)
    {
        builder.Append("<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, metadata.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">민감정보 포함: ");
        Html(builder, metadata.SensitiveValuesIncluded ? "예" : "아니요");
        builder.Append(" · 이 HTML은 외부 리소스와 스크립트를 포함하지 않습니다.</p></section>");
    }

    private static void AppendSummaryGrid(
        StringBuilder builder,
        LocalDiagnosticReport report)
    {
        builder.Append("<div class=\"grid\">");

        builder.Append("<section class=\"card\"><h2>실행 정보</h2><table class=\"kv\">");
        Row(builder, "앱 버전", report.Metadata.ApplicationVersion);
        Row(builder, "운영체제", report.Metadata.OperatingSystem);
        Row(builder, "런타임", report.Metadata.RuntimeVersion);
        Row(builder, "문화권", report.Metadata.Culture);
        Row(builder, "스키마", report.SchemaVersion);
        builder.Append("</table></section>");

        builder.Append("<section class=\"card\"><h2>WLAN</h2><table class=\"kv\">");
        Row(builder, "연결", report.Wlan.IsConnected ? "연결됨" : "연결 안 됨");
        Row(builder, "인터페이스", report.Wlan.InterfaceDescription);
        Row(builder, "상태", report.Wlan.InterfaceState);
        Row(builder, "SSID", report.Wlan.Ssid);
        Row(builder, "BSSID", report.Wlan.Bssid);
        Row(builder, "RSSI", Unit(report.Wlan.RssiDbm, "dBm"));
        Row(builder, "신호 품질", Unit(report.Wlan.SignalQualityPercent, "%"));
        Row(
            builder,
            "밴드 / 채널",
            $"{report.Wlan.Band} / {Number(report.Wlan.Channel, "확인 불가")}");
        Row(builder, "중심 주파수", Unit(report.Wlan.CenterFrequencyMhz, "MHz"));
        Row(builder, "PHY", report.Wlan.PhyType);
        Row(
            builder,
            "Rx / Tx 링크",
            $"{Unit(report.Wlan.ReceiveLinkMbps, "Mbps")} / {Unit(report.Wlan.TransmitLinkMbps, "Mbps")}");
        Row(
            builder,
            "인증 / 암호화",
            $"{report.Wlan.Authentication} / {report.Wlan.Cipher}");
        builder.Append("</table>");
        if (!string.IsNullOrWhiteSpace(report.Wlan.ReadError))
        {
            builder.Append("<p class=\"small\">부분 제한: ");
            Html(builder, report.Wlan.ReadError);
            builder.Append("</p>");
        }
        builder.Append("</section>");

        builder.Append("<section class=\"card\"><h2>프록시 설정</h2><table class=\"kv\">");
        Row(builder, "읽기", report.Proxy.ReadSucceeded ? "성공" : "실패");
        Row(builder, "방식", report.Proxy.Mode);
        Row(builder, "자동 감지", report.Proxy.AutoDetectEnabled ? "사용" : "미사용");
        Row(builder, "PAC", report.Proxy.PacConfigured ? "설정됨" : "없음");
        Row(
            builder,
            "수동 프록시",
            report.Proxy.ManualProxyConfigured ? "설정됨" : "없음");
        Row(builder, "바이패스", report.Proxy.BypassConfigured ? "설정됨" : "없음");
        Row(builder, "Win32 오류", Number(report.Proxy.Win32Error, "없음"));
        builder.Append("</table><p class=\"small\">");
        Html(builder, report.Proxy.Statement);
        builder.Append("</p></section></div>");
    }

    private static void AppendStructuredMeasurements(
        StringBuilder builder,
        IReadOnlyList<ReportMeasurementSection> measurements)
    {
        builder.Append("<section class=\"card\"><h2>구조화 다운로드 측정</h2>");
        if (measurements.Count == 0)
        {
            builder.Append("<p>현재 앱 실행 중 완료된 내부·외부 다운로드 측정이 없습니다.</p></section>");
            return;
        }

        foreach (ReportMeasurementSection measurement in measurements)
        {
            builder.Append("<article class=\"measure\"><h3>");
            Html(builder, measurement.TargetName);
            builder.Append("</h3><p><span class=\"badge\">");
            Html(builder, measurement.PathKind);
            builder.Append("</span> <span class=\"badge\">");
            Html(builder, measurement.Status);
            builder.Append("</span> <span class=\"badge ");
            Html(builder, ConfidenceCss(measurement.Confidence));
            builder.Append("\">신뢰도 ");
            Html(builder, measurement.Confidence);
            builder.Append("</span></p><table class=\"kv\">");
            Row(
                builder,
                "측정 시각",
                $"{LocalTime(measurement.StartedAt)} ~ {LocalTime(measurement.CompletedAt)}");
            Row(builder, "소요 시간", Unit(measurement.DurationSeconds, "초"));
            Row(builder, "수신량", FormatBytes(measurement.BytesReceived));
            Row(
                builder,
                "평균 / 최고 처리량",
                $"{Unit(measurement.AverageMbps, "Mbps")} / {Unit(measurement.PeakMbps, "Mbps")}");
            Row(
                builder,
                "TTFB",
                Unit(measurement.TimeToFirstByteMilliseconds, "ms"));
            Row(
                builder,
                "HTTP / 프록시",
                $"{Number(measurement.HttpStatusCode, "없음")} / {Boolean(measurement.ProxyWasUsed)}");
            Row(
                builder,
                "스트림",
                $"{measurement.StreamsCompleted}/{measurement.StreamsRequested}");
            Row(builder, "리다이렉트", $"{measurement.RedirectCount}회");
            Row(builder, "캐시 판정", measurement.CacheClassification);
            Row(builder, "최종 URL", measurement.FinalUrl);
            Row(builder, "오류 코드", measurement.ErrorCode ?? "없음");
            builder.Append("</table><p>");
            Html(builder, measurement.Message);
            builder.Append("</p><p class=\"small\"><strong>신뢰도 근거:</strong> ");
            Html(
                builder,
                measurement.ConfidenceReasons.Count == 0
                    ? "추가 근거 없음"
                    : string.Join(" · ", measurement.ConfidenceReasons));
            builder.Append("</p>");

            if (measurement.ResponseMetadata.Count > 0)
            {
                builder.Append("<h3>응답 메타데이터</h3><table class=\"kv\">");
                foreach ((string name, string value) in measurement.ResponseMetadata
                             .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Row(builder, name, value);
                }
                builder.Append("</table>");
            }

            if (measurement.Samples.Count > 0)
            {
                builder.Append("<h3>처리량 샘플</h3><div class=\"samples\"><table><thead><tr><th>스트림</th><th>경과</th><th>구간 수신량</th><th>Mbps</th></tr></thead><tbody>");
                foreach (ReportThroughputSample sample in measurement.Samples)
                {
                    builder.Append("<tr><td>");
                    Html(builder, sample.StreamIndex.ToString(CultureInfo.InvariantCulture));
                    builder.Append("</td><td>");
                    Html(builder, $"{sample.OffsetSeconds:F2}초");
                    builder.Append("</td><td>");
                    Html(builder, FormatBytes(sample.IntervalBytes));
                    builder.Append("</td><td>");
                    Html(builder, $"{sample.Mbps:F1}");
                    builder.Append("</td></tr>");
                }
                builder.Append("</tbody></table></div>");
            }

            builder.Append("</article>");
        }

        builder.Append("</section>");
    }

    private static void AppendLegacyMeasurementSection(
        StringBuilder builder,
        IReadOnlyList<ReportTextSection> measurements)
    {
        if (measurements.Count == 0)
        {
            return;
        }

        builder.Append("<section class=\"card\"><h2>화면 진단 문구</h2>");
        foreach (ReportTextSection section in measurements)
        {
            builder.Append("<h3>");
            Html(builder, section.Title);
            builder.Append("</h3><p class=\"small\">");
            Html(builder, LocalTime(section.CapturedAt));
            builder.Append("</p><pre>");
            Html(builder, section.Content);
            builder.Append("</pre>");
        }
        builder.Append("</section>");
    }

    private static void AppendObservationSection(
        StringBuilder builder,
        ReportObservationSection? observation)
    {
        if (observation is null)
        {
            return;
        }

        builder.Append("<section class=\"card\"><h2>브라우저 다운로드 관찰</h2><table class=\"kv\">");
        Row(builder, "상태", observation.Status);
        Row(builder, "관찰 시간", Unit(observation.ObservedSeconds, "초"));
        Row(builder, "백그라운드 기준", Unit(observation.BaselineReceiveMbps, "Mbps"));
        Row(
            builder,
            "평균 / 최고",
            $"{Unit(observation.AverageAdjustedReceiveMbps, "Mbps")} / {Unit(observation.PeakAdjustedReceiveMbps, "Mbps")}");
        Row(builder, "수신량", FormatBytes(observation.TotalReceiveBytes));
        Row(
            builder,
            "일시 정지 / 급락",
            $"{Number(observation.PauseCount, "0")} / {Number(observation.SuddenDropCount, "0")}");
        Row(builder, "BSSID 변경", Number(observation.BssidChangeCount, "0"));
        Row(builder, "인터페이스 변경", Number(observation.AdapterChangeCount, "0"));
        Row(builder, "카운터 재설정", Number(observation.CounterResetCount, "0"));
        Row(builder, "신뢰도", observation.Confidence);
        builder.Append("</table><p>");
        Html(builder, observation.Message);
        builder.Append("</p><p class=\"small\">");
        Html(builder, observation.Limitation);
        builder.Append("</p>");

        if (observation.Samples.Count > 0)
        {
            builder.Append("<h3>시간축 샘플</h3><div class=\"samples\"><table><thead><tr><th>시각</th><th>구간</th><th>수신</th><th>RSSI</th><th>Rx 링크</th><th>이벤트</th></tr></thead><tbody>");
            foreach (ReportObservationSample sample in observation.Samples)
            {
                builder.Append("<tr><td>");
                Html(builder, sample.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                builder.Append("</td><td>");
                Html(builder, sample.IsBaseline ? "기준" : "관찰");
                builder.Append("</td><td>");
                Html(
                    builder,
                    Unit(
                        sample.IsBaseline
                            ? sample.RawReceiveMbps
                            : sample.AdjustedReceiveMbps,
                        "Mbps"));
                builder.Append("</td><td>");
                Html(builder, Unit(sample.RssiDbm, "dBm"));
                builder.Append("</td><td>");
                Html(builder, Unit(sample.ReceiveLinkMbps, "Mbps"));
                builder.Append("</td><td>");
                Html(builder, SampleEvents(sample));
                builder.Append("</td></tr>");
            }
            builder.Append("</tbody></table></div>");
        }

        builder.Append("</section>");
    }

    private static void AppendFindingSection(
        StringBuilder builder,
        IReadOnlyList<ReportFinding> findings)
    {
        builder.Append("<section class=\"card\"><h2>판정</h2>");
        if (findings.Count == 0)
        {
            builder.Append("<p>추가 판정 항목이 없습니다.</p>");
        }
        else
        {
            foreach (ReportFinding finding in findings)
            {
                string severityClass = finding.Severity.Equals(
                    "Critical",
                    StringComparison.OrdinalIgnoreCase)
                    ? "critical"
                    : finding.Severity.Equals(
                        "Warning",
                        StringComparison.OrdinalIgnoreCase)
                        ? "warning"
                        : "information";
                string badgeClass = severityClass switch
                {
                    "critical" => "critical",
                    "warning" => "warn",
                    _ => "info"
                };

                builder.Append("<article class=\"finding ");
                Html(builder, severityClass);
                builder.Append("\"><span class=\"badge ");
                Html(builder, badgeClass);
                builder.Append("\">");
                Html(builder, finding.Severity);
                builder.Append("</span><h3>");
                Html(builder, finding.Title);
                builder.Append("</h3><p><strong>근거:</strong> ");
                Html(builder, finding.Evidence);
                builder.Append("</p><p><strong>해석:</strong> ");
                Html(builder, finding.Interpretation);
                builder.Append("</p><p><strong>다음 확인:</strong> ");
                Html(builder, finding.NextStep);
                builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
                Html(builder, finding.Limitation);
                builder.Append("</p></article>");
            }
        }
        builder.Append("</section>");
    }

    private static void AppendLimitations(
        StringBuilder builder,
        IReadOnlyList<string> limitations)
    {
        builder.Append("<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }
        builder.Append("</ul></section>");
    }

    private static void Row(
        StringBuilder builder,
        string key,
        string? value)
    {
        builder.Append("<tr><th>");
        Html(builder, key);
        builder.Append("</th><td>");
        Html(builder, value ?? string.Empty);
        builder.Append("</td></tr>");
    }

    private static void Html(StringBuilder builder, string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string LocalTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Boolean(bool? value) =>
        value switch
        {
            true => "사용",
            false => "미사용",
            null => "확인 불가"
        };

    private static string ConfidenceCss(string value) =>
        value.Equals("High", StringComparison.OrdinalIgnoreCase)
            ? "high"
            : value.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? "medium"
                : value.Equals("Low", StringComparison.OrdinalIgnoreCase)
                    ? "low"
                    : "info";

    private static string Unit<T>(T? value, string unit)
        where T : struct, IFormattable =>
        value.HasValue
            ? $"{value.Value.ToString(null, CultureInfo.InvariantCulture)} {unit}"
            : "확인 불가";

    private static string Number<T>(T? value, string fallback)
        where T : struct, IFormattable =>
        value.HasValue
            ? value.Value.ToString(null, CultureInfo.InvariantCulture)
            : fallback;

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "확인 불가";
        }

        return FormatBytes(bytes.Value);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024 / 1024:F2} GiB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / 1024d / 1024:F2} MiB";
        }

        return $"{bytes / 1024d:F2} KiB";
    }

    private static string SampleEvents(ReportObservationSample sample)
    {
        List<string> events = [];
        if (sample.BssidChanged)
        {
            events.Add("BSSID 변경");
        }

        if (sample.AdapterChanged)
        {
            events.Add("인터페이스 변경");
        }

        if (sample.CounterReset)
        {
            events.Add("카운터 재설정");
        }

        if (sample.WlanDisconnected)
        {
            events.Add("WLAN 미연결");
        }

        if (sample.PauseDetected)
        {
            events.Add("일시 정지");
        }

        if (sample.SuddenDropDetected)
        {
            events.Add("급락");
        }

        if (!string.IsNullOrWhiteSpace(sample.Note))
        {
            events.Add(sample.Note);
        }

        return events.Count == 0 ? "-" : string.Join(", ", events);
    }
}

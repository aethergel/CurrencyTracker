using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CurrencyTracker.Infos;
using CurrencyTracker.Utilities;

namespace CurrencyTracker.Manager.Transactions;

public static class TransactionsHandler
{
    // Transaction Type Suffix:
    // Inventory - {CurrencyName}.txt
    // Retainer - {CurrencyName}_{RetainerID}.txt
    // Saddle Bag - {CurrencyName}_SB.txt
    // Premium Saddle Bag - {CurrencyName}_PSB.txt
    public static string GetTransactionFilePath(uint CurrencyID, TransactionFileCategory category, ulong ID = 0)
    {
        var suffix = GetTransactionFileSuffix(category, ID);
        var currencyName = CurrencyInfo.GetName(CurrencyID);
        var path = Path.Join(P.PlayerDataFolder, $"{currencyName}{suffix}.txt");
        return Transaction.SanitizeFilePath(path);
    }

    public static string GetTransactionFileSuffix(TransactionFileCategory category, ulong ID = 0) =>
        category switch
        {
            TransactionFileCategory.Inventory => string.Empty,
            TransactionFileCategory.Retainer => $"_{ID}",
            TransactionFileCategory.SaddleBag => "_SB",
            TransactionFileCategory.PremiumSaddleBag => "_PSB",
            _ => string.Empty,
        };

    private static bool ValidityCheck(uint currencyID)
    {
        if (string.IsNullOrEmpty(P.PlayerDataFolder))
        {
            DService.Log.Warning("Player data folder Missed.");
            return false;
        }

        return true;
    }

    // 加载全部记录 Load All Transaction
    public static List<Transaction> LoadAllTransactions(
        uint currencyID, TransactionFileCategory category = 0, ulong ID = 0)
    {
        var filePath = GetTransactionFilePath(currencyID, category, ID);

        return ValidityCheck(currencyID) && File.Exists(filePath)
                   ? Transaction.FromFile(filePath)
                   : [];
    }

    public static async Task<List<Transaction>> LoadAllTransactionsAsync(
        uint currencyID, TransactionFileCategory category = 0, ulong ID = 0)
    {
        var filePath = GetTransactionFilePath(currencyID, category, ID);

        if (ValidityCheck(currencyID) && File.Exists(filePath))
        {
            return await Transaction.FromFileAsync(filePath);
        }

        return [];
    }

    // 加载最新一条记录 Load Latest Transaction
    public static Transaction? LoadLatestSingleTransaction(
        uint currencyID, CharacterInfo? characterInfo = null, TransactionFileCategory category = 0, ulong ID = 0)
    {
        var playerDataFolder = characterInfo != null
                                   ? Path.Join(P.PI.ConfigDirectory.FullName,
                                               $"{characterInfo.Name}_{characterInfo.Server}")
                                   : P.PlayerDataFolder;

        var filePath = characterInfo != null
                           ? Path.Join(playerDataFolder,
                                       $"{CurrencyInfo.GetName(currencyID)}{GetTransactionFileSuffix(category, ID)}.txt")
                           : GetTransactionFilePath(currencyID, category, ID);

        filePath = Transaction.SanitizeFilePath(filePath);

        if (characterInfo == null && !ValidityCheck(currencyID)) return null;
        if (!File.Exists(filePath)) return null;

        var lastLine = File.ReadLines(filePath).Reverse().FirstOrDefault();

        return lastLine == null ? new() : Transaction.FromFileLine(lastLine.AsSpan());
    }

    // 编辑指定记录 Edit Specific Transaction
    public static int EditSpecificTransactions(
        uint currencyID, List<Transaction> selectedTransactions, string locationName = "None",
        string noteContent = "None", TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (selectedTransactions.Count == 0) return 0;

        var editedTransactions = LoadAllTransactions(currencyID, category, ID);
        var filePath = GetTransactionFilePath(currencyID, category, ID);
        var failCount = 0;
        var isLocationEdited = locationName != "None";
        var isNoteEdited = noteContent != "None";

        foreach (var transaction in selectedTransactions)
        {
            var index = editedTransactions.FindIndex(t => t.Equals(transaction));
            if (index == -1)
            {
                failCount++;
                continue;
            }

            if (isLocationEdited) editedTransactions[index].LocationName = locationName;
            if (isNoteEdited) editedTransactions[index].Note = noteContent;
        }

        Transaction.WriteTransactionsToFile(filePath, editedTransactions);

        return failCount;
    }

    // 在数据末尾追加最新一条记录 Append One Transaction
    public static void AppendTransaction(
        uint currencyID, DateTime TimeStamp, long Amount, long Change, string LocationName, string Note,
        TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID)) return;

        var filePath = GetTransactionFilePath(currencyID, category, ID);

        Transaction.AppendTransactionToFile(filePath,
        [
            new()
            {
                TimeStamp = TimeStamp,
                Amount = Amount,
                Change = Change,
                LocationName = LocationName,
                Note = Note
            }
        ]);
    }

    // 新建一条数据记录 Create a New Transaction File with a transaction
    public static void AddTransaction(
        uint currencyID, DateTime timeStamp, long amount, long change, string locationName, string note,
        TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID)) return;

        var filePath = GetTransactionFilePath(currencyID, category, ID);

        Transaction.WriteTransactionsToFile(filePath,
        [
            new()
            {
                TimeStamp = timeStamp,
                Amount = amount,
                Change = change,
                LocationName = locationName,
                Note = note
            }
        ]);
    }

    // 根据时间重新排序文件内记录 Sort Transaction in File by Time
    public static void ReorderTransactions(uint currencyID, TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID)) return;

        Transaction.WriteTransactionsToFile(
            GetTransactionFilePath(currencyID, category, ID),
            [.. LoadAllTransactions(currencyID, category, ID).OrderBy(x => x.TimeStamp)]
        );
    }

    // 按照临界值合并记录 Merge Transaction By Threshold
    public static int MergeTransactionsByLocationAndThreshold(
        uint currencyID, long threshold, bool isOneWayMerge, TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID)) return 0;

        var allTransactions = LoadAllTransactions(currencyID, category, ID);
        if (allTransactions.Count <= 1) return 0;

        var mergedTransactions = new List<Transaction>();
        var mergedCount = 0;

        for (var i = 0; i < allTransactions.Count;)
        {
            var currentTransaction = allTransactions[i];
            var separateMergedCount = 0;

            while (++i < allTransactions.Count &&
                   currentTransaction.LocationName == allTransactions[i].LocationName &&
                   Math.Abs(allTransactions[i].Change) < threshold)
            {
                var nextTransaction = allTransactions[i];

                if (!isOneWayMerge || (isOneWayMerge &&
                                       currentTransaction.Change >= 0 && nextTransaction.Change >= 0) ||
                    (currentTransaction.Change < 0 && nextTransaction.Change < 0))
                {
                    if (nextTransaction.TimeStamp > currentTransaction.TimeStamp)
                    {
                        currentTransaction.Amount = nextTransaction.Amount;
                        currentTransaction.TimeStamp = nextTransaction.TimeStamp;
                    }

                    currentTransaction.Change += nextTransaction.Change;

                    mergedCount += 2;
                    separateMergedCount++;
                }
                else
                    break;
            }

            if (separateMergedCount > 0)
                currentTransaction.Note = $"({Service.Lang.GetText("MergedSpecificHelp", separateMergedCount + 1)})";

            mergedTransactions.Add(currentTransaction);
        }

        Transaction.WriteTransactionsToFile(GetTransactionFilePath(currencyID, category, ID),
                                                      mergedTransactions);
        ReorderTransactions(currencyID, category, ID);

        return mergedCount;
    }

    // 合并特定的记录 Merge Specific Transaction
    public static int MergeSpecificTransactions(
        uint currencyID, string locationName, List<Transaction> selectedTransactions,
        string noteContent = "-1", TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID) || selectedTransactions.Count <= 1) return 0;

        var allTransactions = LoadAllTransactions(currencyID, category, ID);
        var filePath = GetTransactionFilePath(currencyID, category, ID);

        var latestTime = DateTime.MinValue;
        long overallChange = 0;
        long finalAmount = 0;
        var mergedCount = 0;

        foreach (var transaction in selectedTransactions)
        {
            var foundTransaction = allTransactions.FirstOrDefault(t => t.Equals(transaction));

            if (foundTransaction == null) continue;

            if (latestTime < foundTransaction.TimeStamp)
            {
                latestTime = foundTransaction.TimeStamp;
                finalAmount = foundTransaction.Amount;
            }

            overallChange += foundTransaction.Change;
            allTransactions.Remove(foundTransaction);
            mergedCount++;
        }

        var finalTransaction = new Transaction
        {
            TimeStamp = latestTime,
            Change = overallChange,
            LocationName = locationName,
            Amount = finalAmount,
            Note = noteContent != "-1" ? noteContent : $"({Service.Lang.GetText("MergedSpecificHelp", mergedCount)})"
        };

        allTransactions.Add(finalTransaction);
        Transaction.WriteTransactionsToFile(filePath, allTransactions);
        ReorderTransactions(currencyID, category, ID);

        return mergedCount;
    }

    // 导出数据 Export Transaction Data
    public static string ExportData(
        List<Transaction> data, string fileName, uint currencyID, int exportType,
        TransactionFileCategory category = 0, ulong ID = 0)
    {
        if (!ValidityCheck(currencyID)) return "Fail";

        var currencyName = Service.Config.AllCurrencies[currencyID];
        var fileExtension = exportType == 0 ? "csv" : "md";
        var headers = exportType == 0
                          ? Service.Lang.GetText("ExportFileCSVHeader")
                          : $"{Service.Lang.GetText("ExportFileMDHeader")} {currencyName}\n\n{Service.Lang.GetText("ExportFileMDHeader1")}";
        var lineTemplate = exportType == 0 ? "{0},{1},{2},{3},{4}" : "| {0} | {1} | {2} | {3} | {4} |";

        if (exportType != 0 && exportType != 1) return "Fail";

        var playerDataFolder = Path.Combine(P.PlayerDataFolder, "Exported");
        Directory.CreateDirectory(playerDataFolder);

        var nowTime = DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss");
        var finalFileName = string.IsNullOrWhiteSpace(fileName)
                                ? $"{currencyName}_{nowTime}.{fileExtension}"
                                : $"{fileName}_{currencyName}_{nowTime}.{fileExtension}";
        var filePath = Path.Combine(playerDataFolder, finalFileName);

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        writer.WriteLine(headers);
        foreach (var transaction in data)
        {
            var line = string.Format(lineTemplate, transaction.TimeStamp.ToString("yyyy/MM/dd HH:mm:ss"),
                                     transaction.Amount, transaction.Change, transaction.LocationName,
                                     transaction.Note);
            writer.WriteLine(line);
        }

        return filePath;
    }

    // 备份数据 Backup transactions
    public static string BackupTransactions(string dataFolder, int maxBackupFilesCount)
    {
        if (string.IsNullOrEmpty(dataFolder)) return "Fail";

        var backupFolder = Path.Combine(dataFolder, "Backups");
        Directory.CreateDirectory(backupFolder);

        if (maxBackupFilesCount > 0)
        {
            var backupFiles = Directory.GetFiles(backupFolder, "*.zip")
                                       .OrderBy(f => new FileInfo(f).CreationTime)
                                       .ToList();

            while (backupFiles.Count >= maxBackupFilesCount)
            {
                if (!FileHelper.IsFileLocked(new FileInfo(backupFiles[0]))) File.Delete(backupFiles[0]);
                backupFiles.RemoveAt(0);
            }
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        string zipFilePath;
        try
        {
            foreach (var file in Directory.GetFiles(dataFolder))
                File.Copy(file, Path.Combine(tempFolder, Path.GetFileName(file)), true);

            zipFilePath = Path.Combine(backupFolder, $"Backup_{DateTime.Now:yyyyMMddHHmmss}.zip");
            ZipFile.CreateFromDirectory(tempFolder, zipFilePath);
        } finally
        {
            Directory.Delete(tempFolder, true);
        }

        return zipFilePath;
    }

    public static async Task<string> BackupTransactionsAsync(string dataFolder, int maxBackupFilesCount)
    {
        if (string.IsNullOrEmpty(dataFolder)) return "Fail";

        var backupFolder = Path.Combine(dataFolder, "Backups");
        Directory.CreateDirectory(backupFolder);

        if (maxBackupFilesCount > 0)
        {
            var backupFiles = Directory.GetFiles(backupFolder, "*.zip")
                                       .OrderBy(f => new FileInfo(f).CreationTime)
                                       .ToList();

            while (backupFiles.Count >= maxBackupFilesCount)
            {
                var fileInfo = new FileInfo(backupFiles[0]);
                if (!FileHelper.IsFileLocked(fileInfo)) 
                    File.Delete(backupFiles[0]);
                backupFiles.RemoveAt(0);
            }
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolder);

        string zipFilePath;
        try
        {
            foreach (var file in Directory.GetFiles(dataFolder))
            {
                var destFile = Path.Combine(tempFolder, Path.GetFileName(file));
                await using var sourceStream = File.Open(file, FileMode.Open);
                await using var destinationStream = File.Create(destFile);
                await sourceStream.CopyToAsync(destinationStream);
            }

            zipFilePath = Path.Combine(backupFolder, $"Backup_{DateTime.Now:yyyyMMddHHmmss}.zip");
            ZipFile.CreateFromDirectory(tempFolder, zipFilePath);
        }
        finally
        {
            Directory.Delete(tempFolder, true);
        }

        return zipFilePath;
    }

}

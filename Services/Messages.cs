﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Helpers;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Models;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Runtime;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services
{
    public interface IMessages
    {
        MessageList List(
            DateTimeOffset? from,
            DateTimeOffset? to,
            string order,
            int skip,
            int limit,
            string[] devices);
    }

    public class Messages : IMessages
    {
        private const string DATA_PREFIX = "data.";

        private readonly ILogger log;
        private readonly IStorageClient storageClient;

        private readonly DocumentClient documentClient;
        private readonly string databaseName;
        private readonly string collectionId;

        public Messages(
            IServicesConfig config,
            IStorageClient storageClient,
            ILogger logger)
        {
            this.storageClient = storageClient;
            this.documentClient = storageClient.GetDocumentClient();
            this.databaseName = config.MessagesConfig.DocumentDbDatabase;
            this.collectionId = config.MessagesConfig.DocumentDbCollection;
            this.log = logger;
        }

        public MessageList List(
            DateTimeOffset? from,
            DateTimeOffset? to,
            string order,
            int skip,
            int limit,
            string[] devices)
        {
            int dataPrefixLen = DATA_PREFIX.Length;

            var sql = QueryBuilder.GetDocumentsSql(
                "d2cmessage",
                null, null,
                from, "device.msg.received",
                to, "device.msg.received",
                order, "device.msg.received",
                skip,
                limit,
                devices, "device.id");

            this.log.Debug("Created Message Query", () => new { sql });

            FeedOptions queryOptions = new FeedOptions();
            queryOptions.EnableCrossPartitionQuery = true;
            queryOptions.EnableScanInQuery = true;

            List<Document> docs = this.storageClient.QueryDocuments(
                this.databaseName,
                this.collectionId,
                queryOptions,
                sql,
                skip,
                limit);

            // Messages to return
            List<Message> messages = new List<Message>();

            // Auto discovered telemetry types
            HashSet<string> properties = new HashSet<string>();

            foreach (Document doc in docs)
            {
                // Document fields to expose
                JObject data = new JObject();

                // Extract all the telemetry data and types
                var jsonDoc = JObject.Parse(doc.ToString());
                foreach (var item in jsonDoc)
                {
                    // Ignore fields that don't start with "data."
                    if (item.Key.StartsWith(DATA_PREFIX))
                    {
                        // Remove the "data." prefix
                        string key = item.Key.ToString().Substring(dataPrefixLen);
                        data.Add(key, item.Value);

                        // Telemetry types auto-discovery magic
                        properties.Add(key);
                    }
                }

                messages.Add(new Message(
                    doc.GetPropertyValue<string>("device.id"),
                    doc.GetPropertyValue<long>("device.msg.received"),
                    data));
            }

            return new MessageList(messages, new List<string>(properties));
        }
    }
}

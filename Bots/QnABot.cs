// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.BotBuilderSamples
{
    public class QnABot : ActivityHandler
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<QnABot> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IStorage _myStorage;

        // Create cancellation token (used by Async Write operation).
        public CancellationToken cancellationToken { get; private set; }

        // Class for storing a log of utterances (text of messages) as a list.
        public class UtteranceLog : IStoreItem
        {
            // A list of things that users have said to the bot
            public List<string> UtteranceList { get; } = new List<string>();

            // The number of conversational turns that have occurred
            public int TurnNumber { get; set; } = 0;

            // Create concurrency control where this is used.
            public string ETag { get; set; } = "*";
        }

        public QnABot(IConfiguration configuration, ILogger<QnABot> logger, IHttpClientFactory httpClientFactory, IStorage storage)
        {
            if (storage is null) throw new ArgumentNullException();
            _myStorage = storage;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();

            var qnaMaker = new QnAMaker(new QnAMakerEndpoint
            {
                KnowledgeBaseId = _configuration["QnAKnowledgebaseId"],
                EndpointKey = _configuration["QnAEndpointKey"],
                Host = _configuration["QnAEndpointHostName"]
            },
            null,
            httpClient);

            _logger.LogInformation("Calling QnA Maker");

            var options = new QnAMakerOptions { Top = 1 };

            // The actual call to the QnA Maker service.
            var response = await qnaMaker.GetAnswersAsync(turnContext, options);

            // preserve user input.
            var utterance = turnContext.Activity.Text;

            // Make empty local log-items list.
            UtteranceLog logItems = null;

            // See if there are previous messages saved in storage.
            try
            {
                string[] utteranceList = { "UtteranceLog" };
                logItems = _myStorage.ReadAsync<UtteranceLog>(utteranceList).Result?.FirstOrDefault().Value;
            }
            catch
            {
                // Inform the user an error occurred.
                await turnContext.SendActivityAsync("Sorry, something went wrong reading your stored messages!");
            }

            // If no stored messages were found, create and store a new entry.
            if (logItems is null)
            {
                // Add the current utterance to a new object.
                logItems = new UtteranceLog();
                logItems.UtteranceList.Add(utterance);

                // Set initial turn counter to 1.
                logItems.TurnNumber++;

                // Show user new user message.
                await turnContext.SendActivityAsync($"{logItems.TurnNumber}: The list is now: {string.Join(", ", logItems.UtteranceList)}");

                if (response != null && response.Length > 0)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(response[0].Answer), cancellationToken);
                }
                else
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("No QnA Maker answers were found."), cancellationToken);
                }

                // Create dictionary object to hold received user messages.
                var changes = new Dictionary<string, object>();
                {
                    changes.Add("UtteranceLog", logItems);
                }
                try
                {
                    // Save the user message to your Storage.
                    await _myStorage.WriteAsync(changes, cancellationToken);
                }
                catch
                {
                    // Inform the user an error occurred.
                    await turnContext.SendActivityAsync("Sorry, something went wrong storing your message!");
                }
            }
            // Else, our storage already contained saved user messages, add new one to the list.
            else
            {
                // add new message to list of messages to display.
                logItems.UtteranceList.Add(utterance);
                // increment turn counter.
                logItems.TurnNumber++;

                // show user new list of saved messages.
                await turnContext.SendActivityAsync($"{logItems.TurnNumber}: The list is now: {string.Join(", ", logItems.UtteranceList)}");

                if (response != null && response.Length > 0)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(response[0].Answer), cancellationToken);
                }
                else
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("No QnA Maker answers were found."), cancellationToken);
                }

                // Create Dictionary object to hold new list of messages.
                var changes = new Dictionary<string, object>();
                {
                    changes.Add("UtteranceLog", logItems);
                };

                try
                {
                    // Save new list to your Storage.
                    await _myStorage.WriteAsync(changes, cancellationToken);
                }
                catch
                {
                    // Inform the user an error occurred.
                    await turnContext.SendActivityAsync("Sorry, something went wrong storing your message!");
                }
            }

            
        }
    }
}

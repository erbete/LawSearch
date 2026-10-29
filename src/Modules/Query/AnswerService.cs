using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed record AnswerResult(Guid ConversationId, string Answer, IReadOnlyList<Paragraph> Sources);

// Answers a question from start to finish: fetches the conversation, rewrites follow-up
// questions so search understands them, finds the paragraphs, lets the language model
// write the answer from them, and saves the question and answer to the conversation.
internal sealed class AnswerService(
    SearchService search,
    QuestionRewriter rewriter,
    AnswerGenerator generator,
    QueryRepository repository,
    IOptions<QuerySettings> settings)
{
    public async Task<AnswerResult?> AnswerAsync(string question, Guid? conversationId, CancellationToken ct)
    {
        IReadOnlyList<HistoryEntry> history = [];
        string? rewrittenQuestion = null;
        string query = question;

        if (conversationId.HasValue)
        {
            history = await repository.GetHistoryAsync(conversationId.Value, settings.Value.HistoryLimit, ct);

            if (history.Count == 0)
            {
                return null;
            }

            rewrittenQuestion = await rewriter.RewriteAsync(question, history, ct);
            query = rewrittenQuestion;
        }

        Guid id = conversationId ?? Guid.CreateVersion7();

        var paragraphs = await search.SearchAsync(query, ct);
        string answer = await generator.GenerateAsync(question, history, paragraphs, ct);

        await repository.SaveHistoryAsync(id, question, rewrittenQuestion, answer, ct);

        return new AnswerResult(id, answer, paragraphs);
    }
}
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NServiceBus.Transport;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    public class DocumentIdsRepository(CorrespondenceDBContext dbContext) : IDocumentIdsRepository
    {
        // Add implementations for the missing interface members

        public async Task AddDocumentIdAsync(string documentId)
        {
            try
            {
                var newDocumentId = new DocumentIdEntity
                {
                    DocIds = documentId,
                    DocIdStatus = "New",
                    RequestCreationTS = DateTime.UtcNow
                };

                await dbContext.DocumentIds.AddAsync(newDocumentId);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error adding new DocumentIdEntity: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Atomically claims the next available DocId by selecting and marking it as "Used" in a single SQL statement.
        /// This prevents race conditions where two concurrent requests could claim the same DocId.
        /// </summary>
        public async Task<string?> GetAndUpdateDocumentIdAsync()
        {
            try
            {
                var claimed = await dbContext.Database.SqlQueryRaw<string>(
                    @"WITH cte AS (
                        SELECT TOP (1) *
                        FROM DocumentIds WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE DocIdStatus = 'New'
                        ORDER BY SeqId ASC
                    )
                    UPDATE cte
                    SET DocIdStatus = 'Used',
                    RequestCreationTS = GETUTCDATE()
                    OUTPUT inserted.DocIds;"
                )
               .ToListAsync();

                return claimed.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error claiming DocumentId: {ex}");
                throw;
            }
        }

        public async Task<int> GetNewDocumentIdCountAsync()
        {
            try
            {
                var count = await dbContext.DocumentIds
                    .CountAsync(d => d.DocIdStatus == "New");
                return count;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error counting DocumentIdEntities with status 'New': {ex}");
                throw;
            }
        }

        public async Task RevertDocumentIdAsync(string documentId)
        {
            try
            {
                var entity = await dbContext.DocumentIds
                    .FirstOrDefaultAsync(d => d.DocIds == documentId);

                if (entity != null)
                {
                    entity.DocIdStatus = "New";
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reverting DocumentIdEntity with DocIds '{documentId}': {ex}");
                throw;
            }
        }
    }
}

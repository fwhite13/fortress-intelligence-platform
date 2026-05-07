import { braveWebSearch } from './search-client.js';

/**
 * Search the web via Brave Search API.
 * @param {object} user - Decoded JWT claims (not used for this service-level tool)
 * @param {object} params
 * @param {string} params.query - Search query
 * @param {number} [params.count=10] - Number of results (max 20)
 * @param {string} [params.country='US'] - 2-letter country code
 */
export async function webSearch(user, { query, count = 10, country = 'US' }) {
  const results = await braveWebSearch({ query, count, country });

  const webResults = results?.web?.results ?? [];
  return webResults.map(r => ({
    title: r.title,
    url: r.url,
    description: r.description,
    age: r.age ?? null,
  }));
}

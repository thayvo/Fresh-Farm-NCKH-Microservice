# Recommendation Evaluation Summary

- Generated at (UTC): 2026-03-31T04:02:27.4076160Z
- BFF base URL: https://localhost:7085

## home_cold_start

- Status: 200
- Algorithm: content_based_home_v1
- Signal source: 
- Content signal source: catalog_content_v1
- Fallback reason: preference:no_tracked_preference_interactions+collaborative:no_preference_seed_for_collaborative
- Item count: 4
- Top item ids: 8, 1, 29, 17
- Top seller ids: 47, 2, 4, 51
- Unique seller count: 4
- Signal breakdown:
  - content: catalog_content_v1
  - overall: 
  - overallReason: preference:no_tracked_preference_interactions+collaborative:no_preference_seed_for_collaborative
  - preference: 
  - preferenceReason: no_tracked_preference_interactions
  - collaborative: 
  - collaborativeReason: no_preference_seed_for_collaborative

## search_hot_keyword_rau

- Status: 200
- Algorithm: hybrid_search_v1
- Signal source: materialized_cf_v1
- Content signal source: catalog_keyword_v1
- Fallback reason: 
- Item count: 5
- Top item ids: 1, 3, 114, 103, 18
- Top seller ids: 2, 2, 4, 4, 51
- Unique seller count: 3
- Signal breakdown:
  - content: catalog_keyword_v1
  - overall: materialized_cf_v1
  - overallReason: 

## search_cold_keyword_mini

- Status: 200
- Algorithm: keyword_relevance_v1
- Signal source: 
- Content signal source: catalog_keyword_v1
- Fallback reason: no_tracked_keyword_sessions
- Item count: 3
- Top item ids: 105, 118, 11
- Top seller ids: 4, 4, 47
- Unique seller count: 2
- Signal breakdown:
  - content: catalog_keyword_v1
  - overall: 
  - overallReason: no_tracked_keyword_sessions

## similar_hybrid_seed_1

- Status: 200
- Algorithm: hybrid_similar_v1
- Signal source: materialized_cf_v1
- Content signal source: catalog_content_v1
- Fallback reason: 
- Item count: 4
- Top item ids: 4, 102, 18, 16
- Top seller ids: 2, 4, 51, 51
- Unique seller count: 3
- Signal breakdown:
  - content: catalog_content_v1
  - overall: materialized_cf_v1
  - overallReason: 
  - preference: 
  - preferenceReason: 
  - collaborative: materialized_cf_v1
  - collaborativeReason: 

## similar_content_seed_105

- Status: 200
- Algorithm: content_based_similar_v1
- Signal source: 
- Content signal source: catalog_content_v1
- Fallback reason: no_collaborative_interactions_for_seed_product
- Item count: 4
- Top item ids: 37, 6, 7, 36
- Top seller ids: 4, 2, 47, 4
- Unique seller count: 3
- Signal breakdown:
  - content: catalog_content_v1
  - overall: 
  - overallReason: no_collaborative_interactions_for_seed_product
  - preference: 
  - preferenceReason: 
  - collaborative: 
  - collaborativeReason: no_collaborative_interactions_for_seed_product


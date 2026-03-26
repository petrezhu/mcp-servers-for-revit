using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class QueryBudgetTruncationStages
{
    public const string GroupItems = "group_items";
    public const string MemberPreviews = "member_previews";
    public const string NonEssentialFields = "non_essential_fields";
    public const string CountsAndSamples = "counts_and_samples";
}

public sealed class QueryBudgetService
{
    private enum BudgetTier
    {
        Full = 0,
        Compact = 1,
        Tight = 2,
        Minimal = 3
    }

    public SelectionRootsResponse ApplySelectionRootsBudget(
        SelectionRootsResponse response,
        SelectionRootsRequest request)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (request == null) throw new ArgumentNullException(nameof(request));

        var clone = CloneSelectionRootsResponse(response);
        var tier = ResolveBudgetTier(request.TokenBudgetHint);
        var stage = (string) null;
        var truncated = false;

        var groupLimit = ResolveRootGroupLimit(request.LimitGroups, tier);
        if (clone.Groups.Count > groupLimit)
        {
            clone.Groups = clone.Groups.Take(groupLimit).ToList();
            truncated = true;
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.GroupItems);
        }

        var itemLimit = ResolveRootItemLimit(request.LimitItemsPerGroup, tier);
        foreach (var group in clone.Groups)
        {
            if (group.Items.Count > itemLimit)
            {
                group.Items = group.Items.Take(itemLimit).ToList();
                truncated = true;
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.GroupItems);
            }
        }

        if (tier >= BudgetTier.Tight)
        {
            foreach (var group in clone.Groups)
            {
                foreach (var item in group.Items)
                {
                    if (!string.IsNullOrWhiteSpace(item.UniqueId))
                    {
                        item.UniqueId = null;
                        truncated = true;
                    }
                }
            }

            if (truncated)
            {
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.NonEssentialFields);
            }
        }

        if (tier == BudgetTier.Minimal && truncated)
        {
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.CountsAndSamples);
        }

        clone.Truncated = truncated;
        clone.Budget = BuildBudgetMetadata(truncated, stage, truncated ? "open_object_member_groups" : null);
        return clone;
    }

    public ObjectMemberGroupsResponse ApplyObjectMemberGroupsBudget(
        ObjectMemberGroupsResponse response,
        ObjectMemberGroupsRequest request)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (request == null) throw new ArgumentNullException(nameof(request));

        var clone = CloneObjectMemberGroupsResponse(response);
        var tier = ResolveBudgetTier(request.TokenBudgetHint);
        var stage = (string) null;
        var truncated = false;

        var groupLimit = ResolveMemberGroupLimit(request.LimitGroups, tier);
        if (clone.Groups.Count > groupLimit)
        {
            clone.Groups = clone.Groups.Take(groupLimit).ToList();
            truncated = true;
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.GroupItems);
        }

        var previewLimit = ResolveMemberPreviewLimit(request.LimitMembersPerGroup, tier);
        foreach (var group in clone.Groups)
        {
            if (group.TopMembers.Count > previewLimit)
            {
                group.TopMembers = group.TopMembers.Take(previewLimit).ToList();
                group.HasMoreMembers = true;
                truncated = true;
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.MemberPreviews);
            }
        }

        if (tier >= BudgetTier.Tight && !string.IsNullOrWhiteSpace(clone.Title))
        {
            clone.Title = null;
            truncated = true;
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.NonEssentialFields);
        }

        if (tier == BudgetTier.Minimal && truncated)
        {
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.CountsAndSamples);
        }

        clone.Truncated = truncated;
        clone.Budget = BuildBudgetMetadata(truncated, stage, truncated ? "expand_members" : null);
        return clone;
    }

    public NavigateObjectResponse ApplyNavigateObjectBudget(
        NavigateObjectResponse response,
        NavigateObjectRequest request)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (request == null) throw new ArgumentNullException(nameof(request));

        var clone = CloneNavigateObjectResponse(response);
        var tier = ResolveBudgetTier(request.TokenBudgetHint);
        var stage = (string) null;
        var truncated = false;

        var groupLimit = ResolveMemberGroupLimit(request.LimitGroups, tier);
        if (clone.Groups.Count > groupLimit)
        {
            clone.Groups = clone.Groups.Take(groupLimit).ToList();
            truncated = true;
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.GroupItems);
        }

        var previewLimit = ResolveMemberPreviewLimit(request.LimitMembersPerGroup, tier);
        foreach (var group in clone.Groups)
        {
            if (group.TopMembers.Count > previewLimit)
            {
                group.TopMembers = group.TopMembers.Take(previewLimit).ToList();
                group.HasMoreMembers = true;
                truncated = true;
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.MemberPreviews);
            }
        }

        if (tier >= BudgetTier.Tight)
        {
            if (!string.IsNullOrWhiteSpace(clone.Title))
            {
                clone.Title = null;
                truncated = true;
            }

            if (!string.IsNullOrWhiteSpace(clone.ObjectHandle))
            {
                clone.ObjectHandle = null;
                truncated = true;
            }

            if (truncated)
            {
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.NonEssentialFields);
            }
        }

        if (tier == BudgetTier.Minimal && truncated)
        {
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.CountsAndSamples);
        }

        clone.Truncated = truncated;
        clone.Budget = BuildBudgetMetadata(truncated, stage, truncated ? "expand_members" : null);
        return clone;
    }

    public ExpandMembersResponse ApplyExpandMembersBudget(ExpandMembersResponse response, int? tokenBudgetHint)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        var clone = CloneExpandMembersResponse(response);
        var tier = ResolveBudgetTier(tokenBudgetHint);
        var stage = (string) null;
        var truncated = false;

        var expandedLimit = ResolveExpandedMemberLimit(tier);
        if (clone.Expanded.Count > expandedLimit)
        {
            clone.Expanded = clone.Expanded.Take(expandedLimit).ToList();
            truncated = true;
            stage = AdvanceStage(stage, QueryBudgetTruncationStages.CountsAndSamples);
        }

        if (tier >= BudgetTier.Tight)
        {
            foreach (var member in clone.Expanded)
            {
                if (string.Equals(member.ValueKind, "scalar", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(member.DisplayValue))
                {
                    member.DisplayValue = null;
                    truncated = true;
                }
            }

            if (truncated)
            {
                stage = AdvanceStage(stage, QueryBudgetTruncationStages.NonEssentialFields);
            }
        }

        clone.Budget = BuildBudgetMetadata(truncated, stage, truncated ? "navigate_object" : null);
        return clone;
    }

    private static QueryBudgetMetadata BuildBudgetMetadata(bool truncated, string stage, string nextSuggestedAction)
    {
        if (!truncated)
        {
            return new QueryBudgetMetadata
            {
                Truncated = false
            };
        }

        return new QueryBudgetMetadata
        {
            Truncated = true,
            TruncationStage = stage,
            NextSuggestedAction = nextSuggestedAction
        };
    }

    private static BudgetTier ResolveBudgetTier(int? tokenBudgetHint)
    {
        if (!tokenBudgetHint.HasValue || tokenBudgetHint.Value >= 1800)
        {
            return BudgetTier.Full;
        }

        if (tokenBudgetHint.Value >= 1000)
        {
            return BudgetTier.Compact;
        }

        if (tokenBudgetHint.Value >= 400)
        {
            return BudgetTier.Tight;
        }

        return BudgetTier.Minimal;
    }

    private static int ResolveRootGroupLimit(int requestedLimit, BudgetTier tier)
    {
        return tier switch
        {
            BudgetTier.Compact => Math.Min(requestedLimit, 10),
            BudgetTier.Tight => Math.Min(requestedLimit, 5),
            BudgetTier.Minimal => Math.Min(requestedLimit, 3),
            _ => requestedLimit
        };
    }

    private static int ResolveRootItemLimit(int requestedLimit, BudgetTier tier)
    {
        return tier switch
        {
            BudgetTier.Compact => Math.Min(requestedLimit, 10),
            BudgetTier.Tight => Math.Min(requestedLimit, 4),
            BudgetTier.Minimal => Math.Min(requestedLimit, 1),
            _ => requestedLimit
        };
    }

    private static int ResolveMemberGroupLimit(int requestedLimit, BudgetTier tier)
    {
        return tier switch
        {
            BudgetTier.Compact => Math.Min(requestedLimit, 10),
            BudgetTier.Tight => Math.Min(requestedLimit, 6),
            BudgetTier.Minimal => Math.Min(requestedLimit, 3),
            _ => requestedLimit
        };
    }

    private static int ResolveMemberPreviewLimit(int requestedLimit, BudgetTier tier)
    {
        return tier switch
        {
            BudgetTier.Compact => Math.Min(requestedLimit, 8),
            BudgetTier.Tight => Math.Min(requestedLimit, 4),
            BudgetTier.Minimal => Math.Min(requestedLimit, 1),
            _ => requestedLimit
        };
    }

    private static int ResolveExpandedMemberLimit(BudgetTier tier)
    {
        return tier switch
        {
            BudgetTier.Compact => 12,
            BudgetTier.Tight => 6,
            BudgetTier.Minimal => 3,
            _ => int.MaxValue
        };
    }

    private static string AdvanceStage(string currentStage, string candidateStage)
    {
        if (string.IsNullOrWhiteSpace(candidateStage))
        {
            return currentStage;
        }

        if (string.IsNullOrWhiteSpace(currentStage))
        {
            return candidateStage;
        }

        return GetStageRank(candidateStage) >= GetStageRank(currentStage) ? candidateStage : currentStage;
    }

    private static int GetStageRank(string stage)
    {
        return stage switch
        {
            QueryBudgetTruncationStages.GroupItems => 1,
            QueryBudgetTruncationStages.MemberPreviews => 2,
            QueryBudgetTruncationStages.NonEssentialFields => 3,
            QueryBudgetTruncationStages.CountsAndSamples => 4,
            _ => 0
        };
    }

    private static SelectionRootsResponse CloneSelectionRootsResponse(SelectionRootsResponse response)
    {
        return new SelectionRootsResponse
        {
            Source = response.Source,
            TotalRootCount = response.TotalRootCount,
            Truncated = response.Truncated,
            Budget = CloneBudget(response.Budget),
            Groups = response.Groups.Select(group => new RootGroupResult
            {
                GroupKey = group.GroupKey,
                Count = group.Count,
                Items = group.Items.Select(item => new RootItemResult
                {
                    ObjectHandle = item.ObjectHandle,
                    ElementId = item.ElementId,
                    UniqueId = item.UniqueId,
                    Title = item.Title,
                    TypeName = item.TypeName,
                    Category = item.Category
                }).ToList()
            }).ToList()
        };
    }

    private static ObjectMemberGroupsResponse CloneObjectMemberGroupsResponse(ObjectMemberGroupsResponse response)
    {
        return new ObjectMemberGroupsResponse
        {
            ObjectHandle = response.ObjectHandle,
            Title = response.Title,
            Truncated = response.Truncated,
            Budget = CloneBudget(response.Budget),
            Groups = response.Groups.Select(CloneMemberGroup).ToList()
        };
    }

    private static NavigateObjectResponse CloneNavigateObjectResponse(NavigateObjectResponse response)
    {
        return new NavigateObjectResponse
        {
            ValueHandle = response.ValueHandle,
            ObjectHandle = response.ObjectHandle,
            Title = response.Title,
            Truncated = response.Truncated,
            Budget = CloneBudget(response.Budget),
            Groups = response.Groups.Select(CloneMemberGroup).ToList()
        };
    }

    private static ExpandMembersResponse CloneExpandMembersResponse(ExpandMembersResponse response)
    {
        return new ExpandMembersResponse
        {
            ObjectHandle = response.ObjectHandle,
            Budget = CloneBudget(response.Budget),
            Expanded = response.Expanded.Select(member => new ExpandedMemberResult
            {
                DeclaringTypeName = member.DeclaringTypeName,
                MemberName = member.MemberName,
                ValueKind = member.ValueKind,
                DisplayValue = member.DisplayValue,
                CanNavigate = member.CanNavigate,
                ValueHandle = member.ValueHandle,
                ErrorMessage = member.ErrorMessage,
                UsedFallback = member.UsedFallback
            }).ToList()
        };
    }

    private static MemberGroupResult CloneMemberGroup(MemberGroupResult group)
    {
        return new MemberGroupResult
        {
            DeclaringTypeName = group.DeclaringTypeName,
            Depth = group.Depth,
            MemberCount = group.MemberCount,
            HasMoreMembers = group.HasMoreMembers,
            TopMembers = group.TopMembers.ToList()
        };
    }

    private static QueryBudgetMetadata CloneBudget(QueryBudgetMetadata budget)
    {
        if (budget == null)
        {
            return null;
        }

        return new QueryBudgetMetadata
        {
            Truncated = budget.Truncated,
            NextSuggestedAction = budget.NextSuggestedAction,
            TruncationStage = budget.TruncationStage
        };
    }
}

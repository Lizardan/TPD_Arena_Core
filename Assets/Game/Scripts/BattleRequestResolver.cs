namespace TPD.Arena
{
    /// <summary>
    /// Resolves battle request fields into runtime loadouts.
    /// </summary>
    public static class BattleRequestResolver
    {
        public static void ApplyToController(BattleController controller, BattleRequestJson request)
        {
            if (controller == null || request == null)
                return;

            controller.ApplyBattleRequest(request);

            BattleAbilityRegistry registry = controller.BattleAbilityRegistry;
            if (registry == null || !request.HasCustomLoadouts())
                return;

            if (request.leftAbilities != null && request.leftAbilities.Length > 0)
            {
                if (!registry.TryResolveMany(request.leftAbilities, out AbilityDataSO[] leftResolved, out string leftError))
                    throw new System.InvalidOperationException(leftError);

                controller.player1.abilities = leftResolved;
            }

            if (request.rightAbilities != null && request.rightAbilities.Length > 0)
            {
                if (!registry.TryResolveMany(request.rightAbilities, out AbilityDataSO[] rightResolved, out string rightError))
                    throw new System.InvalidOperationException(rightError);

                controller.player2.abilities = rightResolved;
            }
        }
    }
}

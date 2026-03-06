# Feature Branch: Add Power Tiles

## Summary

This feature branch implements two new floating tiles on the net power production page with the following specifications:

1. **First Tile - Current Power Production**:
   - Shows the current power production value
   - Uses a gray to green gradient
   - Gray when production is 0
   - Full green when production is above 2000 W

2. **Second Tile - Power Consumption**:
   - Shows the current power consumption value
   - Uses a gray to red gradient
   - Gray when consumption is below 2000 W
   - Full red when consumption is above 4000 W

## Implementation Details

The implementation was done in `Enphase/EnphaseLocal/Program.cs` by:

1. Adding helper methods to calculate gradient colors:
   - `GetPowerProductionGradient()` for the gray to green gradient
   - `GetPowerConsumptionGradient()` for the gray to red gradient

2. Modifying the `/netpowerproduction` endpoint to:
   - Retrieve both production and consumption data
   - Add two new tile sections with styled gradients
   - Maintain the existing net power production display

3. Updating the HTML structure to include:
   - Two new tile containers with consistent styling
   - Gradient text styling for the values
   - Responsive design improvements

## Files Modified

- `Enphase/EnphaseLocal/Program.cs` - Main implementation
- `Enphase/README.md` - Documentation update

## Testing

- All existing tests continue to pass
- The application builds successfully
- No breaking changes to existing functionality
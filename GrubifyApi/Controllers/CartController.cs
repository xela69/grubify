using Microsoft.AspNetCore.Mvc;
using GrubifyApi.Data;
using GrubifyApi.Models;
using GrubifyApi.Chaos;

namespace GrubifyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        private readonly GrubifyDbContext _db;
        public CartController(GrubifyDbContext db) => _db = db;

        // In-memory cart storage (in production, use database)
        private static readonly Dictionary<string, Cart> UserCarts = new();

        // Only accumulates when the CHAOS_MEMORY_LEAK toggle is on (see AddItemToCart).
        private static readonly List<byte[]> RequestDataCache = new();

        [HttpGet("{userId}")]
        public ActionResult<Cart> GetCart(string userId)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                UserCarts[userId] = new Cart { UserId = userId };
            }
            return Ok(UserCarts[userId]);
        }

        [HttpPost("{userId}/items")]
        public ActionResult<Cart> AddItemToCart(string userId, [FromBody] AddCartItemRequest request)
        {
            // Chaos toggle (default OFF): leak 10MB per call to simulate a runaway cache.
            if (ChaosConfig.MemoryLeakEnabled)
            {
                RequestDataCache.Add(new byte[10 * 1024 * 1024]);
                Console.WriteLine($"[chaos] memory-leak: cache entries={RequestDataCache.Count} (~{RequestDataCache.Count * 10}MB)");
            }

            if (!UserCarts.ContainsKey(userId))
            {
                UserCarts[userId] = new Cart { UserId = userId };
            }

            var cart = UserCarts[userId];
            var existingItem = cart.Items.FirstOrDefault(i => i.FoodItemId == request.FoodItemId);

            if (existingItem != null)
            {
                existingItem.Quantity += request.Quantity;
                existingItem.SpecialInstructions = request.SpecialInstructions;
            }
            else
            {
                var newItem = new CartItem
                {
                    Id = cart.Items.Count + 1,
                    FoodItemId = request.FoodItemId,
                    FoodItem = GetFoodItemById(request.FoodItemId),
                    Quantity = request.Quantity,
                    SpecialInstructions = request.SpecialInstructions
                };
                cart.Items.Add(newItem);
            }

            return Ok(cart);
        }

        [HttpPut("{userId}/items/{itemId}")]
        public ActionResult<Cart> UpdateCartItem(string userId, int itemId, [FromBody] UpdateCartItemRequest request)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                return NotFound("Cart not found");
            }

            var cart = UserCarts[userId];
            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            item.Quantity = request.Quantity;
            item.SpecialInstructions = request.SpecialInstructions;

            return Ok(cart);
        }

        [HttpDelete("{userId}/items/{itemId}")]
        public ActionResult<Cart> RemoveItemFromCart(string userId, int itemId)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                return NotFound("Cart not found");
            }

            var cart = UserCarts[userId];
            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            cart.Items.Remove(item);
            return Ok(cart);
        }

        [HttpDelete("{userId}")]
        public ActionResult ClearCart(string userId)
        {
            if (UserCarts.ContainsKey(userId))
            {
                UserCarts[userId].Items.Clear();
            }
            return Ok();
        }

        // Resolves a food item from Azure SQL for cart line details.
        private FoodItem GetFoodItemById(int foodItemId)
        {
            return _db.FoodItems.Find(foodItemId) ?? new FoodItem();
        }
    }

    public class AddCartItemRequest
    {
        public int FoodItemId { get; set; }
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }

    public class UpdateCartItemRequest
    {
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }
}

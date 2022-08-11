using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

public class IndexModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IRepository<CatalogItem> _itemRepository;

    public IndexModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        IRepository<CatalogItem> itemRepository)
    {
        _basketService = basketService;
        _basketViewModelService = basketViewModelService;
        _itemRepository = itemRepository;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(GetOrSetBasketCookieAndUserName());
    }

    public async Task<IActionResult> OnPost(CatalogItemViewModel productDetails)
    {
        if (productDetails?.Id == null)
        {
            return RedirectToPage("/Index");
        }

        var item = await _itemRepository.GetByIdAsync(productDetails.Id);
        if (item == null)
        {
            return RedirectToPage("/Index");
        }

        var username = GetOrSetBasketCookieAndUserName();
        var basket = await _basketService.AddItemToBasket(username,
            productDetails.Id, item.Price);

        BasketModel = await _basketViewModelService.Map(basket);

        var connectionString = "Endpoint=sb://cloudx-service-bus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=RXa6UVya1lBAabHbTjyAjNjUU9aY/hqrp06K1SjkwYM=";
        var queueName = "orderitemsreserver";
        
        await using var client = new ServiceBusClient(connectionString);
        await using ServiceBusSender sender = client.CreateSender(queueName);
        try
        {
            string messageBody = JsonSerializer.Serialize(BasketModel);
            var message = new ServiceBusMessage(messageBody);
            Console.WriteLine($"Sending message: {messageBody}");
            await sender.SendMessageAsync(message);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
        }
        finally
        {
            await sender.DisposeAsync();
            await client.DisposeAsync();
        }
        
        return RedirectToPage();
    }

    public async Task OnPostUpdate(IEnumerable<BasketItemViewModel> items)
    {
        if (!ModelState.IsValid)
        {
            return;
        }

        var basketView = await _basketViewModelService.GetOrCreateBasketForUser(GetOrSetBasketCookieAndUserName());
        var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
        var basket = await _basketService.SetQuantities(basketView.Id, updateModel);
        BasketModel = await _basketViewModelService.Map(basket);
    }

    private string GetOrSetBasketCookieAndUserName()
    {
        string userName = null;

        if (Request.HttpContext.User.Identity.IsAuthenticated)
        {
            return Request.HttpContext.User.Identity.Name;
        }

        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            userName = Request.Cookies[Constants.BASKET_COOKIENAME];

            if (!Request.HttpContext.User.Identity.IsAuthenticated)
            {
                if (!Guid.TryParse(userName, out var _))
                {
                    userName = null;
                }
            }
        }
        if (userName != null) return userName;

        userName = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions { IsEssential = true };
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, userName, cookieOptions);

        return userName;
    }
}

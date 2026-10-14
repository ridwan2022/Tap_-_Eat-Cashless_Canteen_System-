// Tap & Eat — Internationalization (i18n) & Accessibility (a11y)
// Story 13: Tasks 14.1, 14.2, 14.3, 14.4

const I18N_STORAGE_KEY = "tapandeat.lang";

const translations = {
  en: {
    // Nav & Common
    "brand_title": "🍛 Tap & Eat",
    "nav_menu": "Menu",
    "nav_cart": "Cart",
    "nav_orders": "My Orders",
    "nav_wallet": "Wallet",
    "nav_board": "Token Board",
    "nav_kitchen": "Kitchen",
    "nav_counter": "Counter",
    "nav_admin": "Admin",
    "nav_wallets": "Wallets",
    "nav_inventory": "Inventory",
    "nav_forecast": "Forecast",
    "nav_feedback": "Feedback",
    "nav_notifications": "Notifications",
    "nav_login": "Log in",
    "nav_signup": "Sign up",
    "nav_logout": "Log out",
    "switch_lang": "বাংলা",
// Accessibility
    "skip_link": "Skip to main content",
    "a11y_nav": "Main Navigation",
    "a11y_cart_count": "items in cart",
    "a11y_notifications": "Notifications",
    "a11y_unread_badge": "unread notifications",
    "a11y_voice_search": "Search dishes using voice",

    // Menu
    "menu_title": "Digital Cafeteria Menu",
    "menu_subtitle": "Select your meals and place an order with instant token generation",
    "menu_filter_all": "All",
    "menu_filter_vegetarian": "Vegetarian",
    "menu_filter_nonveg": "Non-Veg",
    "menu_filter_spicy": "Spicy",
    "menu_filter_sweet": "Sweet",
    "menu_available": "In Stock",
    "menu_low_stock": "Low Stock",
    "menu_out_of_stock": "Sold Out",
    "menu_add_to_cart": "Add to Cart",
    "menu_voice_listening": "Listening... speak dish name",
    "menu_voice_btn": "🎤 Voice Order",

    // Cart & Checkout
    "cart_title": "Your Order Cart",
    "cart_empty": "Your cart is empty. Please add dishes from the menu.",
    "cart_subtotal": "Subtotal",
    "cart_total": "Total Amount",
    "cart_checkout": "Proceed to Payment",
    "cart_select_payment": "Select Payment Method",
    "payment_bkash": "bKash (MFS)",
    "payment_nagad": "Nagad (MFS)",
    "payment_wallet": "Employee RFID Wallet",

  // Order & Status
    "status_pending": "Pending Payment",
    "status_paid": "Paid",
    "status_preparing": "Preparing",
    "status_ready": "Ready for Pickup",
    "status_collected": "Collected",
    "status_cancelled": "Cancelled",
    "status_expired": "Expired",
    "token_label": "Token",
    "estimated_prep": "Est. Prep Time",
    "queue_position": "Queue Position",  

    // Notifications
    "notifications_title": "Notifications & Alerts",
    "notif_mark_read": "Mark as read",
    "notif_mark_all_read": "Mark all as read",
    "notif_empty": "No notifications yet.",
    "notif_push_enable": "Enable Browser Push Notifications",
    "notif_push_enabled": "Browser Notifications Enabled",

    // Order History & Subsidy
    "history_title": "Order History & Entitlements",
    "history_filter_all": "All Statuses",
    "history_date_from": "From Date",
    "history_date_to": "To Date",
    "history_filter_btn": "Filter",
    "history_total_spent": "Total Spent",
    "history_page": "Page",
    "subsidy_summary_title": "Subsidy & Allowance Summary",
    "subsidy_credited": "Subsidy Credited",
    "subsidy_spent": "Subsidy Spent",
    "subsidy_balance": "Remaining Balance",
    "subsidy_period_week": "This Week",
    "subsidy_period_month": "This Month",
    "subsidy_period_all": "All Time",

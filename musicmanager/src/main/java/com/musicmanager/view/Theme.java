package com.musicmanager.view;

import java.awt.Color;
import java.awt.Font;

/**
 * shared design tokens for the Swing UI: a light, single-accent flat design
 * with rounded corners and one shared window width across all screens.
 */
public final class Theme {

    // colors
    public static final Color BACKGROUND = Color.decode("#F5F6FA");
    public static final Color SURFACE = Color.decode("#ECEEF2");
    public static final Color CARD_BACKGROUND = Color.decode("#FFFFFF");
    public static final Color BORDER = Color.decode("#E2E5EC");
    public static final Color PRIMARY = Color.decode("#4F46E5");
    public static final Color PRIMARY_HOVER = Color.decode("#4338CA");
    public static final Color SECONDARY = Color.decode("#33363F");
    public static final Color SECONDARY_HOVER = Color.decode("#24262D");
    public static final Color TEXT_PRIMARY = Color.decode("#1A1D23");
    public static final Color TEXT_SECONDARY = Color.decode("#6B7280");
    public static final Color DISABLED = Color.decode("#C7CBD1");
    public static final Color SUCCESS = Color.decode("#2563EB"); // blue, not green, for "Converted" status text
    public static final Color ERROR = Color.decode("#DC2626");

    // typography
    public static final String FONT_FAMILY = Font.SANS_SERIF;
    public static final int TITLE_SIZE = 20;
    public static final int HEADER_SIZE = 15;
    public static final int BODY_SIZE = 13;
    public static final int BUTTON_SIZE = 14;
    public static final int STATUS_SIZE = 11;

    // spacing
    public static final int WINDOW_PADDING = 24;
    public static final int COMPONENT_GAP = 16;
    public static final int BUTTON_PADDING_H = 24;
    public static final int BUTTON_PADDING_V = 12;
    public static final int CARD_GAP = 12;

    // window sizing, shared across all 3 windows so they visually belong together
    public static final int COMMON_WIDTH = 560;
    public static final int HOME_HEIGHT = 260;
    public static final int SONG_LIST_HEIGHT = 600;
    public static final int SETTINGS_WIDTH = 560;
    public static final int SETTINGS_HEIGHT = 360;

    // corner radius; RoundRectangle2D's arc arguments are the full diameter, not the radius
    public static final int CORNER_RADIUS = 12;
    public static final int CORNER_ARC = CORNER_RADIUS * 2;

    public static Font titleFont() {
        return new Font(FONT_FAMILY, Font.BOLD, TITLE_SIZE);
    }

    public static Font headerFont() {
        return new Font(FONT_FAMILY, Font.PLAIN, HEADER_SIZE);
    }

    public static Font bodyFont() {
        return new Font(FONT_FAMILY, Font.PLAIN, BODY_SIZE);
    }

    public static Font statusFont() {
        return new Font(FONT_FAMILY, Font.BOLD, STATUS_SIZE);
    }

    private Theme() {
    }
}

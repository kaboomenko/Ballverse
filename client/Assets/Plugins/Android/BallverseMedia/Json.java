package com.ballverse.media;

import org.json.JSONException;
import org.json.JSONObject;

/** Fluent JSONObject builder without checked exceptions. Field names are the wire contract with C# NativeJson DTOs. */
final class Json {
    private final JSONObject object = new JSONObject();

    Json put(String key, Object value) {
        try {
            if (value instanceof Double && (((Double) value).isNaN() || ((Double) value).isInfinite())) value = 0.0;
            object.put(key, value == null ? JSONObject.NULL : value);
        } catch (JSONException e) {
            throw new IllegalArgumentException(e);
        }
        return this;
    }

    JSONObject object() {
        return object;
    }

    @Override
    public String toString() {
        return object.toString();
    }
}
